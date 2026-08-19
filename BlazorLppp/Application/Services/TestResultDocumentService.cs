using System.Text;
using System.Text.RegularExpressions;

using BlazorLppp.Application.Models;
using BlazorLppp.Data;
using BlazorLppp.Domain.Entities;
using BlazorLppp.Domain.Enums;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlazorLppp.Application.Services;

public partial class TestResultDocumentService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IWebHostEnvironment environment,
    IOptions<DocumentStorageOptions> documentOptions) : ITestResultDocumentService
{
    private const string FontName = "Times New Roman";
    private const string BodyFontSize = "24"; // 12 pt
    private const string TitleFontSize = "28"; // 14 pt

    public async Task<string> GenerateAsync(
        TestAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var document = attempt.TestDocumentId.HasValue
            ? await dbContext.TestDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == attempt.TestDocumentId.Value, cancellationToken)
            : null;

        var questions = await dbContext.TestQuestions
            .AsNoTracking()
            .Include(q => q.Options)
            .Where(q => attempt.TestDocumentId.HasValue && q.TestDocumentId == attempt.TestDocumentId.Value)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(cancellationToken);

        var answers = await dbContext.TestAnswers
            .AsNoTracking()
            .Include(a => a.SelectedOption)
            .Where(a => a.TestAttemptId == attempt.Id)
            .ToListAsync(cancellationToken);

        var answersByQuestion = answers.ToDictionary(a => a.TestQuestionId);

        var baseName = BuildFileBaseName(attempt.LastName, attempt.FirstName, attempt.MiddleName);
        var root = ResolveResultsRoot();
        var folderPath = Path.Combine(root, baseName);
        Directory.CreateDirectory(folderPath);

        var fileName = $"{baseName}.docx";
        var absolutePath = Path.Combine(folderPath, fileName);

        await using (var stream = new FileStream(
            absolutePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            using var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
            var mainPart = word.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            var fullName = $"{attempt.LastName} {attempt.FirstName} {attempt.MiddleName}".Trim();
            var examDate = (attempt.CompletedAt ?? attempt.StartedAt).ToString("dd.MM.yyyy");
            var isAdaptivity200 = Adaptivity200Document.IsAdaptivity200(document, questions);
            var isZbroya = ZbroyaScoring.CanScore(document, questions);
            var isNpna = NpnaScoring.CanScore(document, questions);

            if (isAdaptivity200)
            {
                var scoring = Adaptivity200Scoring.Evaluate(questions, answersByQuestion);
                AppendAdaptivity200Blank(body, attempt, fullName, examDate, questions, answersByQuestion, scoring);
            }
            else if (isZbroya)
            {
                var scoring = ZbroyaScoring.Evaluate(questions, answersByQuestion);
                AppendZbroyaBlank(body, attempt, fullName, examDate, questions, answersByQuestion, scoring);
            }
            else if (isNpna)
            {
                var scoring = NpnaScoring.Evaluate(questions, answersByQuestion);
                AppendNpnaBlank(body, attempt, fullName, examDate, questions, answersByQuestion, scoring);
            }
            else
            {
                AppendCenteredParagraph(body, "Реєстраційний бланк", bold: true, fontSize: TitleFontSize);
                AppendEmptyParagraph(body);

                AppendFieldLine(body, [("П.І.Б. (повністю)", fullName)]);
                AppendFieldLine(body,
                [
                    ("Дата обстеження", examDate),
                    ("Вік", string.Empty),
                    ("Стать", string.Empty)
                ]);
                AppendFieldLine(body, [("Посада (підрозділ)", attempt.NumberUnit.ToString())]);
                AppendFieldLine(body,
                [
                    ("Спеціальність", string.Empty),
                    ("Військове звання", string.Empty)
                ]);
                AppendEmptyParagraph(body);

                var instruction = !string.IsNullOrWhiteSpace(document?.Instruction)
                    ? document.Instruction
                    : "Вам будуть запропоновані твердження, які стосуються Вашого здоров’я та характеру. Якщо Ви згодні з твердженням, поставте знак “+” у графі “Так” в реєстраційному бланку, якщо ні – поставте знак “-” у графі “Ні”. Над відповідями намагайтеся довго не замислюватися, правильних або неправильних відповідей немає.";

                AppendInstructionParagraph(body, instruction);
                AppendEmptyParagraph(body);

                var isHorska = HorskaScoring.CanScore(document, questions);
                var isAssinger = AssingerScoring.CanScore(document, questions);
                body.AppendChild(isAssinger
                    ? BuildAssingerAnswersTable(questions, answersByQuestion)
                    : isHorska
                        ? BuildScaleAnswersTable(questions, answersByQuestion)
                        : BuildAnswersTable(questions, answersByQuestion));

                if (isHorska)
                {
                    var scoring = HorskaScoring.Evaluate(questions, answersByQuestion);
                    AppendHorskaScoringSection(body, scoring);
                }
                else if (SuicideRiskScoring.CanScore(document, questions))
                {
                    var scoring = SuicideRiskScoring.Evaluate(questions, answersByQuestion);
                    AppendScoringSection(body, scoring);
                }
                else if (isAssinger)
                {
                    var scoring = AssingerScoring.Evaluate(questions, answersByQuestion);
                    AppendAssingerScoringSection(body, scoring);
                }
            }

            body.AppendChild(CreateSectionProperties());

            mainPart.Document.Save();
        }

        return Path.Combine(baseName, fileName).Replace('\\', '/');
    }

    public string GetAbsolutePath(string relativePath)
    {
        var root = ResolveResultsRoot();
        var combined = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Некоректний шлях до файлу результату.");
        }

        return combined;
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Task.CompletedTask;
        }

        var absolutePath = GetAbsolutePath(relativePath);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        var folderPath = Path.GetDirectoryName(absolutePath);
        var root = ResolveResultsRoot();
        if (!string.IsNullOrWhiteSpace(folderPath) &&
            !string.Equals(folderPath, root, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(folderPath) &&
            !Directory.EnumerateFileSystemEntries(folderPath).Any())
        {
            Directory.Delete(folderPath);
        }

        return Task.CompletedTask;
    }

    public Task<(string AbsolutePath, string DownloadFileName)> CombineResultDocumentsAsync(
        IReadOnlyList<string> resultRelativePaths,
        string? fileNameHint = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (resultRelativePaths is null || resultRelativePaths.Count == 0)
        {
            throw new InvalidOperationException("Немає файлів результатів для об'єднання.");
        }

        var sourcePaths = new List<string>(resultRelativePaths.Count);
        foreach (var relative in resultRelativePaths)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var absolute = GetAbsolutePath(relative);
            if (File.Exists(absolute))
            {
                sourcePaths.Add(absolute);
            }
        }

        if (sourcePaths.Count == 0)
        {
            throw new InvalidOperationException("Файли результатів не знайдено на диску.");
        }

        var exportRoot = Path.Combine(ResolveResultsRoot(), "_exports");
        Directory.CreateDirectory(exportRoot);

        var safeHint = SanitizeFileName(string.IsNullOrWhiteSpace(fileNameHint)
            ? "Результати"
            : fileNameHint.Trim());
        var downloadFileName = $"{safeHint}_{DateTime.Now:yyyyMMdd-HHmmss}.docx";
        var absolutePath = Path.Combine(exportRoot, downloadFileName);

        using (var stream = new FileStream(
            absolutePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            useAsync: false))
        {
            using var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
            var mainPart = word.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            var isFirst = true;
            foreach (var sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!isFirst)
                {
                    body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                }

                isFirst = false;

                using var source = WordprocessingDocument.Open(sourcePath, false);
                var sourceBody = source.MainDocumentPart?.Document?.Body;
                if (sourceBody is null)
                {
                    continue;
                }

                foreach (var element in sourceBody.Elements())
                {
                    if (element is SectionProperties)
                    {
                        continue;
                    }

                    body.AppendChild(element.CloneNode(true));
                }
            }

            if (isFirst)
            {
                throw new InvalidOperationException("Не вдалося прочитати жодного файлу результату.");
            }

            body.AppendChild(CreateSectionProperties());
            mainPart.Document.Save();
        }

        return Task.FromResult((absolutePath, downloadFileName));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var cleaned = new string(chars).Trim('.', ' ', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? "Результати" : cleaned;
    }

    public string BuildFileBaseName(string lastName, string firstName, string middleName)
    {
        var surname = SanitizeSegment(lastName);
        var firstInitial = Initial(firstName);
        var middleInitial = Initial(middleName);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(surname))
        {
            parts.Add(surname);
        }

        if (!string.IsNullOrWhiteSpace(firstInitial))
        {
            parts.Add(firstInitial);
        }

        if (!string.IsNullOrWhiteSpace(middleInitial))
        {
            parts.Add(middleInitial);
        }

        return parts.Count == 0
            ? $"result-{DateTime.Now:yyyyMMdd-HHmmss}"
            : string.Join('_', parts);
    }

    private string ResolveResultsRoot()
    {
        var documentsRoot = documentOptions.Value.RootPath;
        var documentsAbsolute = Path.IsPathRooted(documentsRoot)
            ? documentsRoot
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, documentsRoot));

        var appData = Path.GetDirectoryName(documentsAbsolute)
            ?? Path.Combine(environment.ContentRootPath, "App_Data");

        return Path.GetFullPath(Path.Combine(appData, "Results"));
    }

    private static SectionProperties CreateSectionProperties()
        => new(
            new PageSize { Width = 11906, Height = 16838 }, // A4
            new PageMargin
            {
                Top = 720,
                Right = 850,
                Bottom = 720,
                Left = 850,
                Header = 360,
                Footer = 360
            });

    private static void AppendNpnaBlank(
        Body body,
        TestAttempt attempt,
        string fullName,
        string examDate,
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion,
        NpnaScoringResult scoring)
    {
        AppendCenteredParagraph(
            body,
            NpnaDocumentTemplate.CanonicalTitle,
            bold: true,
            fontSize: TitleFontSize);
        AppendCenteredParagraph(body, "Реєстраційний бланк", bold: true, fontSize: BodyFontSize);
        AppendEmptyParagraph(body);

        AppendFieldLine(body, [("П.І.Б. (повністю)", fullName)]);
        AppendFieldLine(body,
        [
            ("Дата обстеження", examDate),
            ("Вік", string.Empty),
            ("Стать", string.Empty)
        ]);
        AppendFieldLine(body, [("Посада (підрозділ)", attempt.NumberUnit.ToString())]);
        AppendFieldLine(body,
        [
            ("Спеціальність", string.Empty),
            ("Військове звання", string.Empty)
        ]);
        AppendEmptyParagraph(body);

        AppendInstructionParagraph(body, NpnaDocumentTemplate.CanonicalInstruction);
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            "Позначення в сітці: «+» — відповідь «Так», «−» — відповідь «Ні».",
            bold: true);
        body.AppendChild(BuildAdaptivityAnswerGrid(questions, answersByQuestion, 276, 20));
        AppendEmptyParagraph(body);

        AppendNpnaScaleScoresLine(body, scoring);
        AppendNpnaScoringSection(body, scoring);

        AppendEmptyParagraph(body);
        AppendCenteredParagraph(body, "Відповіді за питаннями", bold: true, fontSize: TitleFontSize);
        AppendEmptyParagraph(body);
        body.AppendChild(BuildAnswersTable(questions, answersByQuestion));
    }

    private static void AppendNpnaScaleScoresLine(Body body, NpnaScoringResult scoring)
    {
        if (scoring.IsUnscorable)
        {
            AppendBodyParagraph(
                body,
                $"Відповідей «Так/Ні»: {scoring.AnsweredCount} із {NpnaDocumentTemplate.QuestionCount}. " +
                "Сирі бали та стени не розраховуються.",
                bold: true);
            return;
        }

        var raw = string.Join(
            "  ",
            scoring.Scales.Select(s => $"{s.Key}{s.Raw}"));
        var stens = string.Join(
            "  ",
            scoring.Scales.Select(s => $"{s.Key}{s.Sten}"));

        AppendBodyParagraph(body, $"Сирі бали: {raw}", bold: true);
        AppendBodyParagraph(body, $"Стени: {stens}", bold: true);
        AppendBodyParagraph(
            body,
            $"Відповідей «Так/Ні»: {scoring.AnsweredCount} із {NpnaDocumentTemplate.QuestionCount}.");
    }

    private static void AppendAdaptivity200Blank(
        Body body,
        TestAttempt attempt,
        string fullName,
        string examDate,
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion,
        Adaptivity200ScoringResult scoring)
    {
        AppendFieldLine(body,
        [
            ("П.І.Б.", string.IsNullOrWhiteSpace(fullName) ? string.Empty : fullName),
            ("Підрозділ", attempt.NumberUnit.ToString()),
            ("Дата", examDate)
        ]);

        var titleParagraph = new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Right },
                new SpacingBetweenLines { After = "120" }),
            CreateRun("Адаптивність 200", bold: true, TitleFontSize));
        body.AppendChild(titleParagraph);
        AppendEmptyParagraph(body);

        body.AppendChild(BuildAdaptivityAnswerGrid(questions, answersByQuestion, 200, 20));
        AppendEmptyParagraph(body);

        AppendAdaptivityScaleScoresLine(body, scoring);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(
            body,
            "Позначення: «+» — відповідь «Так», «−» — відповідь «Ні».");

        AppendAdaptivity200ScoringSection(body, scoring);

        AppendEmptyParagraph(body);
        AppendCenteredParagraph(body, "Відповіді за питаннями", bold: true, fontSize: TitleFontSize);
        AppendEmptyParagraph(body);
        body.AppendChild(BuildAnswersTable(questions, answersByQuestion));
    }

    private static void AppendAdaptivity200ScoringSection(Body body, Adaptivity200ScoringResult scoring)
    {
        AppendEmptyParagraph(body);
        AppendCenteredParagraph(body, "Оцінка результатів", bold: true, fontSize: TitleFontSize);
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            "Обробка виконана за ключем методики «Адаптивність-200» (БОО). " +
            "Кожний збіг відповіді з ключем шкали = 1 бал.");

        AppendBodyParagraph(
            body,
            $"Д (достовірність) = {scoring.ReliabilityD} — {scoring.ReliabilityLevelName}.",
            bold: true);

        if (scoring.IsResultUnreliable)
        {
            AppendEmptyParagraph(body);
            AppendBodyParagraph(body, "Психологічний висновок", bold: true);
            AppendBodyParagraph(body, scoring.Conclusion);
            return;
        }

        AppendBodyParagraph(
            body,
            $"ПР = {scoring.BehavioralRegulationPr} ({scoring.StenPr} стенів); " +
            $"КП = {scoring.CommunicativePotentialKp} ({scoring.StenKp} стенів); " +
            $"МН = {scoring.MoralNormativityMn} ({scoring.StenMn} стенів).");

        AppendBodyParagraph(
            body,
            $"ОАП = ПР + КП + МН = {scoring.PersonalAdaptationPotentialOap} " +
            $"({scoring.StenOap} стенів).",
            bold: true);

        AppendBodyParagraph(
            body,
            $"ВПС = {scoring.MilitaryOrientationVps} ({scoring.StenVps} стенів); " +
            $"ДАП = {scoring.DeviantPropensityDap} ({scoring.StenDap} стенів); " +
            $"СР = {scoring.SuicidalRiskSr} ({scoring.StenSrDisplay} стенів).");

        if (scoring.StenSr is null)
        {
            AppendBodyParagraph(
                body,
                "Примітка: для СР = 0 у джерельній таблиці стенів зазначено і 9, і 10 — " +
                "однозначне переведення потребує уточнення методики.");
        }

        AppendEmptyParagraph(body);
        AppendBodyParagraph(body, "Психологічний висновок", bold: true);
        AppendBodyParagraph(body, scoring.Conclusion);
    }

    /// <summary>
    /// Формат бланка: «Д4  ПР53  КП16  МН15  ВПС19  ДАП24  СР11»
    /// (без пробілу між літерою і числом, два пробіли між шкалами).
    /// </summary>
    private static void AppendAdaptivityScaleScoresLine(Body body, Adaptivity200ScoringResult scoring)
    {
        var parts = new[]
        {
            $"Д{scoring.ReliabilityD}",
            $"ПР{scoring.BehavioralRegulationPr}",
            $"КП{scoring.CommunicativePotentialKp}",
            $"МН{scoring.MoralNormativityMn}",
            $"ВПС{scoring.MilitaryOrientationVps}",
            $"ДАП{scoring.DeviantPropensityDap}",
            $"СР{scoring.SuicidalRiskSr}"
        };

        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { After = "80", Line = "276", LineRule = LineSpacingRuleValues.Auto }),
            CreateRun(string.Join("  ", parts), bold: false, BodyFontSize)));
    }

    private static Table BuildAdaptivityAnswerGrid(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion,
        int totalItems = 200,
        int columns = 20)
    {
        var marksByOrder = questions.ToDictionary(
            q => q.SortOrder,
            q =>
            {
                answersByQuestion.TryGetValue(q.Id, out var answer);
                var (yesMark, noMark, _) = ResolveMarks(q, answer);
                if (!string.IsNullOrWhiteSpace(yesMark))
                {
                    return "+";
                }

                if (!string.IsNullOrWhiteSpace(noMark))
                {
                    return "−";
                }

                return string.Empty;
            });

        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                CreateBorder<TopBorder>(),
                CreateBorder<LeftBorder>(),
                CreateBorder<BottomBorder>(),
                CreateBorder<RightBorder>(),
                CreateBorder<InsideHorizontalBorder>(),
                CreateBorder<InsideVerticalBorder>()),
            new TableLayout { Type = TableLayoutValues.Fixed }));

        var rows = (int)Math.Ceiling(totalItems / (double)columns);
        const string cellWidth = "480";

        var grid = new TableGrid();
        for (var c = 0; c < columns; c++)
        {
            grid.AppendChild(new GridColumn { Width = cellWidth });
        }

        table.AppendChild(grid);

        for (var row = 0; row < rows; row++)
        {
            var tableRow = new TableRow();
            for (var col = 0; col < columns; col++)
            {
                var number = row * columns + col + 1;
                if (number > totalItems)
                {
                    tableRow.AppendChild(CreateCell(string.Empty, center: true, width: cellWidth));
                    continue;
                }

                marksByOrder.TryGetValue(number, out var mark);
                var text = string.IsNullOrWhiteSpace(mark)
                    ? number.ToString()
                    : $"{number} {mark}";
                tableRow.AppendChild(CreateCell(text, center: true, bold: !string.IsNullOrWhiteSpace(mark), width: cellWidth));
            }

            table.AppendChild(tableRow);
        }

        return table;
    }

    private static void AppendZbroyaBlank(
        Body body,
        TestAttempt attempt,
        string fullName,
        string examDate,
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion,
        ZbroyaScoringResult scoring)
    {
        var nameLine = string.IsNullOrWhiteSpace(fullName)
            ? "Шановний________________________________________________________________"
            : $"Шановний {fullName}";
        AppendBodyParagraph(body, nameLine, bold: true);
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            "Пропонуємо Вам перевірити свою готовність до несення служби зі зброєю в добовому наряді. " +
            "Ваші правдиві і відверті відповіді сприятимуть прийняттю об’єктивного рішення про допуск Вас до виконання зазначених завдань.");
        AppendEmptyParagraph(body);

        AppendBodyParagraph(body, "1. Інструкція.", bold: true);
        AppendBodyParagraph(
            body,
            "Прочитайте уважно кожне з наведених нижче речень і обведіть відповідну цифру праворуч залежно від того, " +
            "ЯК ВИ ПОЧУВАЄТЕСЯ ЦІЄЇ МИТІ. Над запитаннями довго не замислюйтеся, тому що правильних чи неправильних відповідей немає.");
        AppendEmptyParagraph(body);

        body.AppendChild(BuildZbroyaRatingTable(questions, answersByQuestion));
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            "2. Поставте позначку на шкалі, яка відповідає інтенсивності вияву зазначених чинників:",
            bold: true);
        AppendEmptyParagraph(body);

        body.AppendChild(BuildZbroyaSanScaleTable(questions, answersByQuestion));
        AppendEmptyParagraph(body);

        AppendBodyParagraph(body, "Чи згодні Ви із зазначеним висловом?:", bold: true);
        AppendBodyParagraph(
            body,
            "“Я маю необхідні знання і практичні навички, вивчив функціональні обов’язки, пройшов відповідний інструктаж, " +
            "його вимоги мені зрозумілі, проблемних питань щодо організації несення служби не маю. " +
            "Мій стан здоров’я, настрій, самопочуття і активність, морально-психологічний стан дозволяють мені виконувати " +
            "службові обов’язки із зброєю. Проблемних питань, негативних чинників впливу на мій морально-психологічний стан не маю. " +
            "Готовий нести службу зі зброєю”.");
        AppendEmptyParagraph(body);

        var readinessMark = scoring.ReadyForWeaponDuty switch
        {
            true => "Так",
            false => "Ні",
            null => "____"
        };
        AppendBodyParagraph(body, $"Відповідь: {readinessMark}", bold: true);
        AppendBodyParagraph(body, "Якщо згодні поставте свій підпис і дату:");
        AppendBodyParagraph(
            body,
            "_______________________________________________________________________________________________________ (посада, в/звання, прізвище та ініціали)");
        AppendBodyParagraph(
            body,
            string.IsNullOrWhiteSpace(fullName)
                ? "_______________________________________________________________________________________________________"
                : fullName);
        AppendBodyParagraph(body, FormatUkrainianDateLine(examDate));
        AppendEmptyParagraph(body);
        AppendBodyParagraph(body, "Якщо ні, то зазначте причини:_____________________________________________________________________________");
        AppendEmptyParagraph(body);

        AppendBodyParagraph(body, "4. Напишіть про Ваші пропозиції щодо покращення умов служби:_______________________________________________________________________________________________________________________________________________________________________________________________________");
        AppendEmptyParagraph(body);

        AppendBodyParagraph(body, "Обстеження провів:");
        AppendBodyParagraph(
            body,
            "______________________________________________________________________________________________________");
        AppendBodyParagraph(body, "(посада, в/звання, прізвище та ініціали)");
        AppendBodyParagraph(body, FormatUkrainianDateLine(examDate));
        AppendEmptyParagraph(body);

        AppendCenteredParagraph(body, "РЕЗУЛЬТАТИ ОБСТЕЖЕННЯ", bold: true, fontSize: TitleFontSize);
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            $"Реактивна тривожність. 1. РТ = Σ1 {scoring.SumDirect} − Σ2 {scoring.SumCalm} + 35 = {scoring.ReactiveAnxiety} " +
            $"({scoring.AnxietyLevelName}).");
        AppendBodyParagraph(
            body,
            "Σ1 – сума відповідей по пунктам шкали (№3, 4, 6, 7, 12, 13, 14, 17, 18)");
        AppendBodyParagraph(
            body,
            "Σ2 – сума решти відповідей (№ 1, 2, 5, 10, 11, 15, 16, 19, 20)");
        AppendBodyParagraph(
            body,
            "(Низька – до 30; помірна – від 31 до 45; висока – понад 46;)");
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            $"2. Індекс САН = (С + А + Н) / 3 × 100% = ({scoring.WellBeingPercent}% + {scoring.ActivityPercent}% + {scoring.MoodPercent}%) / 3 " +
            $"= {scoring.SanIndex:0.#}% ({scoring.SanLevelName} рівень).");
        AppendBodyParagraph(
            body,
            "(Низький – [0;20]; середній – ]20;60]; високий – ]60;100])");
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            "3. Наявність негативних чинників впливу на МПС ________________________________________________________________________________________________________________________________________________________________________________________________________________");
        AppendEmptyParagraph(body);

        AppendBodyParagraph(body, "ВИСНОВКИ:", bold: true);
        AppendBodyParagraph(body, scoring.Conclusion);
        AppendEmptyParagraph(body);
        AppendBodyParagraph(
            body,
            $"Підрозділ: {attempt.NumberUnit}. Дата обстеження: {examDate}.");
    }

    private static string FormatUkrainianDateLine(string examDate)
    {
        // examDate expected as dd.MM.yyyy
        var parts = examDate.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3)
        {
            return $"“{parts[0]}” __________________ 20{parts[2][^2..]} р.";
        }

        return $"“_______” __________________ 20______ р. ({examDate})";
    }

    private static Table BuildZbroyaRatingTable(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                CreateBorder<TopBorder>(),
                CreateBorder<LeftBorder>(),
                CreateBorder<BottomBorder>(),
                CreateBorder<RightBorder>(),
                CreateBorder<InsideHorizontalBorder>(),
                CreateBorder<InsideVerticalBorder>()),
            new TableLayout { Type = TableLayoutValues.Fixed }));

        const string statementWidth = "5200";
        const string optionWidth = "1100";
        table.AppendChild(new TableGrid(
            new GridColumn { Width = statementWidth },
            new GridColumn { Width = optionWidth },
            new GridColumn { Width = optionWidth },
            new GridColumn { Width = optionWidth },
            new GridColumn { Width = optionWidth }));

        var header = new TableRow();
        header.AppendChild(CreateCell("Речення", bold: true, center: true, width: statementWidth));
        header.AppendChild(CreateCell("Ні, це не так", bold: true, center: true, width: optionWidth));
        header.AppendChild(CreateCell("Мабуть так", bold: true, center: true, width: optionWidth));
        header.AppendChild(CreateCell("Правильно", bold: true, center: true, width: optionWidth));
        header.AppendChild(CreateCell("Абсолютно правильно", bold: true, center: true, width: optionWidth));
        table.AppendChild(header);

        foreach (var question in questions.Where(q => q.SortOrder is >= 1 and <= 20).OrderBy(q => q.SortOrder))
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            var selected = ResolveZbroyaRating(question, answer);

            var row = new TableRow();
            row.AppendChild(CreateCell($"{question.SortOrder}. {question.Text}", width: statementWidth));
            for (var value = 1; value <= 4; value++)
            {
                var isSelected = selected == value;
                var text = isSelected ? $"({value})" : value.ToString();
                row.AppendChild(CreateMarkedCell(text, isSelected, optionWidth));
            }

            table.AppendChild(row);
        }

        return table;
    }

    private static Table BuildZbroyaSanScaleTable(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                CreateBorder<TopBorder>(),
                CreateBorder<LeftBorder>(),
                CreateBorder<BottomBorder>(),
                CreateBorder<RightBorder>(),
                CreateBorder<InsideHorizontalBorder>(),
                CreateBorder<InsideVerticalBorder>()),
            new TableLayout { Type = TableLayoutValues.Fixed }));

        const string cellWidth = "880";
        var grid = new TableGrid();
        for (var i = 0; i < 11; i++)
        {
            grid.AppendChild(new GridColumn { Width = cellWidth });
        }

        table.AppendChild(grid);

        AppendSanScaleBlock(table, "САМОПОЧУТТЯ", ResolveSanPercent(questions, answersByQuestion, 21), cellWidth);
        AppendSanScaleBlock(table, "АКТИВНІСТЬ", ResolveSanPercent(questions, answersByQuestion, 22), cellWidth);
        AppendSanScaleBlock(table, "НАСТРІЙ", ResolveSanPercent(questions, answersByQuestion, 23), cellWidth);

        return table;
    }

    private static void AppendSanScaleBlock(Table table, string title, int? percent, string cellWidth)
    {
        var titleRow = new TableRow();
        titleRow.AppendChild(CreateSpannedCell(title, bold: true, center: true, width: cellWidth, span: 11));
        table.AppendChild(titleRow);

        var scaleRow = new TableRow();
        for (var step = 0; step <= 10; step++)
        {
            var value = step * 10;
            var label = step switch
            {
                0 => "0%",
                5 => "50",
                10 => "100%",
                _ => $"{value}"
            };
            var isSelected = percent == value;
            var text = isSelected ? $"[{label}]" : label;
            scaleRow.AppendChild(CreateMarkedCell(text, isSelected, cellWidth));
        }

        table.AppendChild(scaleRow);
    }

    private static int? ResolveSanPercent(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion,
        int sortOrder)
    {
        var question = questions.FirstOrDefault(q => q.SortOrder == sortOrder);
        if (question is null)
        {
            return null;
        }

        answersByQuestion.TryGetValue(question.Id, out var answer);
        if (answer?.ScaleValue is null)
        {
            return null;
        }

        return Math.Clamp(answer.ScaleValue.Value, 0, 10) * 10;
    }

    private static int? ResolveZbroyaRating(TestQuestion question, TestAnswer? answer)
    {
        if (answer is null)
        {
            return null;
        }

        var key = answer.SelectedOption?.Key?.Trim();
        if (int.TryParse(key, out var fromKey) && fromKey is >= 1 and <= 4)
        {
            return fromKey;
        }

        var text = answer.SelectedOption?.Text?.Trim() ?? string.Empty;
        if (text.StartsWith("Ні", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("не так", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (text.Contains("Мабуть", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (text.Equals("Правильно", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (text.Contains("Абсолютно", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (!string.IsNullOrWhiteSpace(key) && key.Length == 1)
        {
            var letter = char.ToUpperInvariant(key[0]);
            return letter switch
            {
                'A' or 'А' => 1,
                'B' or 'Б' => 2,
                'C' or 'В' => 3,
                'D' or 'Г' => 4,
                _ => null
            };
        }

        return null;
    }

    private static TableCell CreateMarkedCell(string text, bool selected, string width)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { Before = "40", After = "40", Line = "240", LineRule = LineSpacingRuleValues.Auto },
                new Justification { Val = JustificationValues.Center }),
            CreateRun(text, bold: selected, BodyFontSize));

        var properties = new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width },
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

        if (selected)
        {
            properties.AppendChild(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = "D9D9D9"
            });
        }

        return new TableCell(properties, paragraph);
    }

    private static TableCell CreateSpannedCell(string text, bool bold, bool center, string width, int span)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { Before = "40", After = "40", Line = "240", LineRule = LineSpacingRuleValues.Auto },
                new Justification
                {
                    Val = center ? JustificationValues.Center : JustificationValues.Left
                }),
            CreateRun(text, bold, BodyFontSize));

        return new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width },
                new GridSpan { Val = span },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
            paragraph);
    }

    private static Table BuildScaleAnswersTable(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                CreateBorder<TopBorder>(),
                CreateBorder<LeftBorder>(),
                CreateBorder<BottomBorder>(),
                CreateBorder<RightBorder>(),
                CreateBorder<InsideHorizontalBorder>(),
                CreateBorder<InsideVerticalBorder>()),
            new TableLayout { Type = TableLayoutValues.Fixed }));

        table.AppendChild(new TableGrid(
            new GridColumn { Width = "700" },
            new GridColumn { Width = "7800" },
            new GridColumn { Width = "1200" }));

        var header = new TableRow();
        header.AppendChild(CreateCell("№з/п", bold: true, center: true, width: "700"));
        header.AppendChild(CreateCell("Питання і твердження", bold: true, center: true, width: "7800"));
        header.AppendChild(CreateCell("Бал", bold: true, center: true, width: "1200"));
        table.AppendChild(header);

        foreach (var question in questions)
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            var mark = answer?.ScaleValue?.ToString() ?? string.Empty;
            var row = new TableRow();
            row.AppendChild(CreateCell(question.SortOrder.ToString(), center: true, width: "700"));
            row.AppendChild(CreateCell(question.Text, width: "7800"));
            row.AppendChild(CreateCell(mark, center: true, bold: !string.IsNullOrWhiteSpace(mark), width: "1200"));
            table.AppendChild(row);
        }

        return table;
    }

    private static Table BuildAssingerAnswersTable(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                CreateBorder<TopBorder>(),
                CreateBorder<LeftBorder>(),
                CreateBorder<BottomBorder>(),
                CreateBorder<RightBorder>(),
                CreateBorder<InsideHorizontalBorder>(),
                CreateBorder<InsideVerticalBorder>()),
            new TableLayout { Type = TableLayoutValues.Fixed }));

        table.AppendChild(new TableGrid(
            new GridColumn { Width = "600" },
            new GridColumn { Width = "5200" },
            new GridColumn { Width = "700" },
            new GridColumn { Width = "700" },
            new GridColumn { Width = "700" },
            new GridColumn { Width = "1800" }));

        var header = new TableRow();
        header.AppendChild(CreateCell("№з/п", bold: true, center: true, width: "600"));
        header.AppendChild(CreateCell("Питання", bold: true, center: true, width: "5200"));
        header.AppendChild(CreateCell("1", bold: true, center: true, width: "700"));
        header.AppendChild(CreateCell("2", bold: true, center: true, width: "700"));
        header.AppendChild(CreateCell("3", bold: true, center: true, width: "700"));
        header.AppendChild(CreateCell("Відповідь", bold: true, center: true, width: "1800"));
        table.AppendChild(header);

        foreach (var question in questions.OrderBy(q => q.SortOrder))
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            var choice = AssingerScoring.ResolveScore(question, answer);
            var optionText = string.Empty;
            if (choice is >= 1 and <= 3)
            {
                var option = question.Options
                    .OrderBy(o => o.SortOrder)
                    .FirstOrDefault(o => o.SortOrder == choice || o.Key == choice.Value.ToString());
                optionText = string.IsNullOrWhiteSpace(option?.Text)
                    ? choice.Value.ToString()
                    : $"{choice}. {option.Text.Trim()}";
            }

            var row = new TableRow();
            row.AppendChild(CreateCell(question.SortOrder.ToString(), center: true, width: "600"));
            row.AppendChild(CreateCell(question.Text, width: "5200"));
            row.AppendChild(CreateCell(choice == 1 ? "+" : string.Empty, center: true, bold: choice == 1, width: "700"));
            row.AppendChild(CreateCell(choice == 2 ? "+" : string.Empty, center: true, bold: choice == 2, width: "700"));
            row.AppendChild(CreateCell(choice == 3 ? "+" : string.Empty, center: true, bold: choice == 3, width: "700"));
            row.AppendChild(CreateCell(optionText, width: "1800"));
            table.AppendChild(row);
        }

        return table;
    }

    private static Table BuildAnswersTable(
        IReadOnlyList<TestQuestion> questions,
        IReadOnlyDictionary<Guid, TestAnswer> answersByQuestion)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                CreateBorder<TopBorder>(),
                CreateBorder<LeftBorder>(),
                CreateBorder<BottomBorder>(),
                CreateBorder<RightBorder>(),
                CreateBorder<InsideHorizontalBorder>(),
                CreateBorder<InsideVerticalBorder>()),
            new TableLayout { Type = TableLayoutValues.Fixed }));

        table.AppendChild(new TableGrid(
            new GridColumn { Width = "700" },
            new GridColumn { Width = "7200" },
            new GridColumn { Width = "900" },
            new GridColumn { Width = "900" }));

        table.AppendChild(CreateHeaderRow());

        foreach (var question in questions)
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            var (yesMark, noMark, fallback) = ResolveMarks(question, answer);
            table.AppendChild(CreateQuestionRow(question, yesMark, noMark, fallback));
        }

        return table;
    }

    private static TableRow CreateHeaderRow()
    {
        var row = new TableRow();
        row.AppendChild(CreateCell("№з/п", bold: true, center: true, width: "700"));
        row.AppendChild(CreateCell("Питання і твердження", bold: true, center: true, width: "7200"));
        row.AppendChild(CreateCell("Так", bold: true, center: true, width: "900"));
        row.AppendChild(CreateCell("Ні", bold: true, center: true, width: "900"));
        return row;
    }

    private static TableRow CreateQuestionRow(
        TestQuestion question,
        string yesMark,
        string noMark,
        string? fallbackAnswer)
    {
        var questionText = question.Text;
        if (!string.IsNullOrWhiteSpace(fallbackAnswer) && !int.TryParse(fallbackAnswer, out _))
        {
            questionText = $"{question.Text} ({fallbackAnswer})";
        }

        var row = new TableRow();
        row.AppendChild(CreateCell(question.SortOrder.ToString(), center: true, width: "700"));
        row.AppendChild(CreateCell(questionText, width: "7200"));
        row.AppendChild(CreateCell(yesMark, center: true, bold: !string.IsNullOrWhiteSpace(yesMark), width: "900"));
        row.AppendChild(CreateCell(noMark, center: true, bold: !string.IsNullOrWhiteSpace(noMark), width: "900"));
        return row;
    }

    private static (string YesMark, string NoMark, string? Fallback) ResolveMarks(
        TestQuestion question,
        TestAnswer? answer)
    {
        if (answer is null)
        {
            return (string.Empty, string.Empty, null);
        }

        if (question.Type is QuestionType.YesNo or QuestionType.SingleChoice)
        {
            var option = answer.SelectedOption
                ?? question.Options.FirstOrDefault(o => o.Id == answer.SelectedOptionId);
            var optionText = option?.Text?.Trim()
                ?? option?.Key?.Trim()
                ?? string.Empty;
            var optionKey = option?.Key?.Trim();

            if (IsYes(optionText) || IsYes(optionKey))
            {
                return ("+", string.Empty, null);
            }

            if (IsNo(optionText) || IsNo(optionKey))
            {
                return (string.Empty, "−", null);
            }

            if (!string.IsNullOrWhiteSpace(optionText) && !int.TryParse(optionText, out _))
            {
                return (string.Empty, string.Empty, optionText);
            }
        }

        if (question.Type == QuestionType.Scale && answer.ScaleValue.HasValue)
        {
            return (string.Empty, string.Empty, answer.ScaleValue.Value.ToString());
        }

        return (string.Empty, string.Empty, null);
    }

    private static bool IsYes(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Equals("Так", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("+", StringComparison.Ordinal));

    private static bool IsNo(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Equals("Ні", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("No", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("-", StringComparison.Ordinal) ||
            value.Equals("–", StringComparison.Ordinal) ||
            value.Equals("−", StringComparison.Ordinal));

    private static TableCell CreateCell(
        string text,
        bool bold = false,
        bool center = false,
        string width = "2000")
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { Before = "40", After = "40", Line = "240", LineRule = LineSpacingRuleValues.Auto },
                new Justification
                {
                    Val = center ? JustificationValues.Center : JustificationValues.Left
                }),
            CreateRun(text, bold, BodyFontSize));

        return new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
            paragraph);
    }

    private static TBorder CreateBorder<TBorder>()
        where TBorder : BorderType, new()
        => new()
        {
            Val = BorderValues.Single,
            Size = 8,
            Space = 0,
            Color = "000000"
        };

    private static void AppendHorskaScoringSection(Body body, HorskaScoringResult scoring)
    {
        AppendEmptyParagraph(body);
        AppendCenteredParagraph(body, "Оцінка результатів", bold: true, fontSize: TitleFontSize);
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            "Обробка виконана за методикою вивчення схильності до суїцидальної поведінки (М.В. Горська). " +
            "Для кожної шкали можлива кількість балів від 0 до 20.");

        AppendScaleScoreParagraph(body, scoring.Anxiety);
        AppendScaleScoreParagraph(body, scoring.Frustration);
        AppendScaleScoreParagraph(body, scoring.Aggression);
        AppendScaleScoreParagraph(body, scoring.Rigidity);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(
            body,
            $"Сумарний показник схильності до суїцидальної поведінки: {scoring.TotalPoints} балів " +
            $"(рівень — {scoring.RiskLevelName}).",
            bold: true);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(body, "Психологічний висновок", bold: true);
        AppendBodyParagraph(body, scoring.Conclusion);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(body, "Орієнтири інтерпретації сумарного показника", bold: true);
        AppendBodyParagraph(body, "0–38 балів — рівень схильності до суїцидальної поведінки низький;");
        AppendBodyParagraph(body, "39–45 балів — рівень схильності до суїцидальної поведінки знаходиться в нормі;");
        AppendBodyParagraph(body, "46 балів і більше — рівень схильності до суїцидальної поведінки високий, потрібна корекційна робота.");
    }

    private static void AppendScaleScoreParagraph(Body body, HorskaScaleScore scale)
    {
        AppendBodyParagraph(
            body,
            $"{scale.Name}: {scale.Points} балів — {scale.LevelName}. {scale.Description}" +
            (string.IsNullOrWhiteSpace(scale.Note) ? string.Empty : $" {scale.Note}"));
    }

    private static void AppendAssingerScoringSection(Body body, AssingerScoringResult scoring)
    {
        AppendEmptyParagraph(body);
        AppendCenteredParagraph(body, "Оцінка результатів", bold: true, fontSize: TitleFontSize);
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            "Обробка виконана за методикою А. Ассингера (оцінка агресивності у відносинах). " +
            "Підраховано суму номерів обраних відповідей (1, 2 або 3).");

        AppendBodyParagraph(
            body,
            $"Сума балів: {scoring.TotalPoints} ({scoring.LevelName}). " +
            $"Відповідей «3»: {scoring.ScoreThreeCount}; відповідей «1»: {scoring.ScoreOneCount}.",
            bold: true);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(body, "Психологічний висновок", bold: true);
        AppendBodyParagraph(body, scoring.Conclusion);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(body, "Орієнтири інтерпретації", bold: true);
        AppendBodyParagraph(body, "45 і більше балів — надмірна агресивність;");
        AppendBodyParagraph(body, "36–44 бали — помірна агресивність;");
        AppendBodyParagraph(body, "35 і менше балів — надмірна миролюбність.");
    }

    private static void AppendNpnaScoringSection(Body body, NpnaScoringResult scoring)
    {
        AppendEmptyParagraph(body);
        AppendCenteredParagraph(body, "Оцінка результатів", bold: true, fontSize: TitleFontSize);
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            "Обробка виконана за сімома ключами особистісного опитувальника «НПН-А». " +
            "Кожний збіг відповіді з ключем оцінюється в один сирий бал, після чого показники переводяться у стени.");

        foreach (var scale in scoring.Scales)
        {
            if (scoring.IsUnscorable)
            {
                break;
            }

            AppendBodyParagraph(
                body,
                $"{scale.Name} ({scale.Key}): {scale.Raw} сирих балів → {scale.Sten} стенів ({scale.LevelName}).");
        }

        AppendEmptyParagraph(body);
        AppendBodyParagraph(
            body,
            scoring.IsUnscorable
                ? "Ключі застосовано не можна: немає відповідей «Так/Ні»."
                : scoring.IsResultUnreliable
                    ? $"Достовірність Д = {scoring.ReliabilityD.Raw} сирих балів ({scoring.ReliabilityD.Sten} стенів) — результат недостовірний (поріг 8)."
                    : $"Достовірність Д = {scoring.ReliabilityD.Raw} сирих балів ({scoring.ReliabilityD.Sten} стенів) — дані можна інтерпретувати.",
            bold: true);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(body, "Психологічний висновок", bold: true);
        AppendBodyParagraph(body, scoring.Conclusion);

        if (!scoring.IsUnscorable)
        {
            AppendEmptyParagraph(body);
            AppendBodyParagraph(body, "Орієнтири інтерпретації стенів", bold: true);
            AppendBodyParagraph(body, "8, 9, 10 стенів — висока вираженість відповідних ознак;");
            AppendBodyParagraph(body, "4, 5, 6, 7 стенів — середні показники, допустима норма;");
            AppendBodyParagraph(body, "менше 4 стенів — практична відсутність указаних ознак.");
            AppendBodyParagraph(
                body,
                "Шкала достовірності: 8 і більше сирих балів — дані недостовірні через соціально бажані відповіді.");
        }
    }

    private static void AppendScoringSection(Body body, SuicideRiskScoringResult scoring)
    {
        AppendEmptyParagraph(body);
        AppendCenteredParagraph(body, "Оцінка результатів", bold: true, fontSize: TitleFontSize);
        AppendEmptyParagraph(body);

        AppendBodyParagraph(
            body,
            "Обробка виконана за ключем методики СР-45 (П.І. Юнацкевіч). Підраховано кількість відповідей, що співпали з ключем.");

        AppendBodyParagraph(
            body,
            $"Шкала «неправди» (L): співпадінь N = {scoring.LieMatches} з {scoring.LieMax}; " +
            $"L = {FormatCoefficient(scoring.LieCoefficient)} (±0,16).");

        AppendBodyParagraph(
            body,
            $"Шкала схильності до суїцидальних реакцій (Sr): співпадінь N = {scoring.RiskMatches} з {scoring.RiskMax}; " +
            $"Sr = {FormatCoefficient(scoring.RiskCoefficient)} (±0,07).");

        AppendEmptyParagraph(body);
        AppendBodyParagraph(
            body,
            $"Оцінка працівника: {scoring.Score} " +
            $"{ScoreWord(scoring.Score)} ({scoring.RiskLevelName} рівень прояву).",
            bold: true);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(body, "Психологічний висновок", bold: true);
        AppendBodyParagraph(body, scoring.Conclusion);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(body, "Примітка щодо достовірності", bold: true);
        AppendBodyParagraph(body, scoring.ReliabilityNote);

        AppendEmptyParagraph(body);
        AppendBodyParagraph(
            body,
            "Методика констатує початковий рівень розвитку схильності особистості до самогубства на момент обстеження. " +
            "За наявності конфліктної ситуації або інших негативних умов ця схильність може змінюватися.");
    }

    private static string FormatCoefficient(double value)
        => value.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("uk-UA"));

    private static string ScoreWord(int score) => score switch
    {
        1 => "бал",
        >= 2 and <= 4 => "бали",
        _ => "балів"
    };

    private static void AppendBodyParagraph(Body body, string text, bool bold = false)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Both },
                new SpacingBetweenLines { After = "80" }),
            CreateRun(text, bold, BodyFontSize)));
    }

    private static void AppendCenteredParagraph(Body body, string text, bool bold = false, string fontSize = BodyFontSize)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { After = "120" }),
            CreateRun(text, bold, fontSize)));
    }

    private static void AppendInstructionParagraph(Body body, string instruction)
    {
        var text = instruction.StartsWith("Інструкція", StringComparison.OrdinalIgnoreCase)
            ? instruction
            : $"Інструкція: {instruction}";

        body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Both },
                new SpacingBetweenLines { After = "120" }),
            CreateRun(text, bold: false, BodyFontSize)));
    }

    private static void AppendFieldLine(Body body, IReadOnlyList<(string Label, string Value)> fields)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { After = "80", Line = "276", LineRule = LineSpacingRuleValues.Auto }));

        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                paragraph.AppendChild(CreateRun("    ", bold: false, BodyFontSize));
            }

            var (label, value) = fields[i];
            paragraph.AppendChild(CreateRun($"{label} ", bold: true, BodyFontSize));

            var filled = string.IsNullOrWhiteSpace(value)
                ? "____________________"
                : value;

            paragraph.AppendChild(CreateRun(filled, bold: false, BodyFontSize));
        }

        body.AppendChild(paragraph);
    }

    private static void AppendEmptyParagraph(Body body)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "60" }),
            CreateRun(string.Empty, bold: false, BodyFontSize)));
    }

    private static Run CreateRun(string text, bool bold, string fontSize)
    {
        var runProperties = new RunProperties(
            new RunFonts { Ascii = FontName, HighAnsi = FontName, ComplexScript = FontName, EastAsia = FontName },
            new FontSize { Val = fontSize },
            new FontSizeComplexScript { Val = fontSize });

        if (bold)
        {
            runProperties.AppendChild(new Bold());
        }

        return new Run(runProperties, new Text(text));
    }

    private static string Initial(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(trimmed[0]).ToString();
    }

    private static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (Path.GetInvalidFileNameChars().Contains(ch) || InvalidNameChars().IsMatch(ch.ToString()))
            {
                builder.Append('-');
                continue;
            }

            builder.Append(ch);
        }

        return MultiDash().Replace(builder.ToString(), "-").Trim('-', '.', ' ', '_');
    }

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex InvalidNameChars();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}
