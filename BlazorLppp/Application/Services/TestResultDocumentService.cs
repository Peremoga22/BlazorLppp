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

            AppendCenteredParagraph(body, "Реєстраційний бланк", bold: true, fontSize: TitleFontSize);
            AppendEmptyParagraph(body);

            var fullName = $"{attempt.LastName} {attempt.FirstName} {attempt.MiddleName}".Trim();
            var examDate = (attempt.CompletedAt ?? attempt.StartedAt).ToString("dd.MM.yyyy");

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

            body.AppendChild(BuildAnswersTable(questions, answersByQuestion));
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
        if (!string.IsNullOrWhiteSpace(fallbackAnswer))
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
            var optionText = answer.SelectedOption?.Text?.Trim()
                ?? answer.SelectedOption?.Key?.Trim()
                ?? string.Empty;

            if (IsYes(optionText) || IsYes(answer.SelectedOption?.Key))
            {
                return ("+", string.Empty, null);
            }

            if (IsNo(optionText) || IsNo(answer.SelectedOption?.Key))
            {
                return (string.Empty, "-", null);
            }

            if (!string.IsNullOrWhiteSpace(optionText))
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
            value.Equals("–", StringComparison.Ordinal));

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
