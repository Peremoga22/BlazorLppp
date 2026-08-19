using System.Text;
using System.Text.RegularExpressions;

using BlazorLppp.Application.Models;
using BlazorLppp.Domain.Enums;

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BlazorLppp.Application.Services;

public partial class TestDocumentParser : ITestDocumentParser
{
    public bool CanParse(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    public ParsedTestDocument Parse(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Файл документа не знайдено.", filePath);
        }

        if (!CanParse(filePath))
        {
            throw new InvalidOperationException(
                "Автоматичний розбір підтримується для файлів .docx, .doc та .txt.");
        }

        var forceAssinger = IsAssingerFileName(filePath);
        var forceNpna = IsNpnaFileName(filePath);
        var forceAnonymous = IsAnonymousSurveyFileName(filePath);

        try
        {
            return ParseCore(filePath, forceAssinger, forceNpna, forceAnonymous);
        }
        catch (Exception) when (forceAssinger)
        {
            return AssingerDocumentTemplate.Create();
        }
        catch (Exception) when (forceNpna)
        {
            return NpnaDocumentTemplate.Create();
        }
        catch (Exception) when (forceAnonymous)
        {
            return AnonymousSurveyDocumentTemplate.Create();
        }
    }

    private ParsedTestDocument ParseCore(string filePath, bool forceAssinger, bool forceNpna, bool forceAnonymous = false)
    {
        ParsedTestDocument parsed;
        if (Path.GetExtension(filePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            parsed = ParseLines(ReadTxtLines(filePath), forceAssinger, forceNpna);
        }
        else if (WordDocConverter.IsDocExtension(filePath))
        {
            try
            {
                var convertedPath = WordDocConverter.ConvertToDocx(filePath);
                try
                {
                    parsed = ParseLines(ReadDocxLines(convertedPath), forceAssinger, forceNpna);
                }
                finally
                {
                    TryDeleteTempFile(convertedPath);
                }
            }
            catch (Exception)
            {
                // На Linux / без Word читаємо текст напряму з OLE .doc
                parsed = ParseLines(DocBinaryTextReader.ReadLines(filePath), forceAssinger, forceNpna);
            }

            // Якщо .doc Адаптивності розібрався погано — беремо канонічний .docx із SeedDocuments.
            if (IsAdaptivity200FileName(filePath) && !IsCompleteAdaptivity200Document(parsed))
            {
                var fallback = ResolveAdaptivity200SeedDocx(filePath);
                if (fallback is not null)
                {
                    parsed = ParseLines(ReadDocxLines(fallback), forceAssinger, forceNpna);
                }
            }
        }
        else
        {
            parsed = ParseLines(ReadDocxLines(filePath), forceAssinger, forceNpna);
        }

        if (IsAdaptivity200FileName(filePath) ||
            IsAdaptivity200Title(parsed.Title) ||
            IsCompleteAdaptivity200Document(parsed))
        {
            parsed.Title = "Адаптивність-200 (БОО)";
            return parsed;
        }

        if (forceAssinger ||
            IsAssingerTitle(parsed.Title) ||
            IsCompleteAssingerDocument(parsed))
        {
            if (IsCompleteAssingerDocument(parsed))
            {
                parsed.Title = AssingerDocumentTemplate.CanonicalTitle;
                parsed.Instruction ??= AssingerDocumentTemplate.CanonicalInstruction;
                return parsed;
            }

            return AssingerDocumentTemplate.Create();
        }

        if (forceNpna ||
            IsNpnaTitle(parsed.Title) ||
            IsCompleteNpnaDocument(parsed))
        {
            if (IsCompleteNpnaDocument(parsed))
            {
                parsed.Title = NpnaDocumentTemplate.CanonicalTitle;
                parsed.Instruction ??= NpnaDocumentTemplate.CanonicalInstruction;
                return parsed;
            }

            return NpnaDocumentTemplate.Create();
        }

        if (forceAnonymous ||
            IsAnonymousSurveyFileName(filePath) ||
            IsAnonymousSurveyTitle(parsed.Title))
        {
            return AnonymousSurveyDocumentTemplate.Create();
        }

        var isZbroyaSource =
            IsZbroyaFileName(filePath) ||
            IsZbroyaTitle(parsed.Title) ||
            LooksLikeZbroyaDocument(parsed);

        if (isZbroyaSource)
        {
            // Беремо розібраний бланк (наприклад Тест_зброя.docx); шаблон — лише якщо неповний.
            if (IsCompleteZbroyaDocument(parsed))
            {
                parsed.Title = "Тест ЗБРОЯ (готовність до служби зі зброєю)";
                return parsed;
            }

            return ZbroyaDocumentTemplate.Create();
        }

        return parsed;
    }

    private static bool IsCompleteZbroyaDocument(ParsedTestDocument parsed)
    {
        var validReactive = parsed.Questions.Count(q =>
            q.SortOrder is >= 1 and <= 20 &&
            q.Text.Length >= 8 &&
            q.Text.Any(char.IsLetter) &&
            q.Options.Count >= 4);

        return validReactive >= 20 && parsed.Questions.Count >= 24;
    }

    private static bool LooksLikeZbroyaDocument(ParsedTestDocument parsed)
    {
        if (IsAdaptivity200Title(parsed.Title) || IsCompleteAdaptivity200Document(parsed))
        {
            return false;
        }

        if (IsNpnaTitle(parsed.Title) || IsCompleteNpnaDocument(parsed))
        {
            return false;
        }

        if (LooksLikeIncompleteZbroya(parsed))
        {
            return true;
        }

        if (parsed.Instruction is not null &&
            (parsed.Instruction.Contains("ПОЧУВАЄТЕСЯ ЦІЄЇ МИТІ", StringComparison.OrdinalIgnoreCase) ||
             parsed.Instruction.Contains("Абсолютно правильно", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return parsed.Questions.Any(q => ZbroyaDocumentTemplate.IsKnownReactiveItem(q.Text));
    }

    private static bool IsZbroyaFileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return name.Contains("ЗБРОЯ", StringComparison.OrdinalIgnoreCase)
               || name.Contains("zbroya", StringComparison.OrdinalIgnoreCase)
               || name.Contains("зброя", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAdaptivity200FileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return name.Contains("адаптивн", StringComparison.OrdinalIgnoreCase)
               || name.Contains("adaptiv", StringComparison.OrdinalIgnoreCase)
               || (name.Contains("БОО", StringComparison.OrdinalIgnoreCase) &&
                   name.Contains("200", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCompleteAdaptivity200Document(ParsedTestDocument parsed)
    {
        if (parsed.Questions.Count is < 180 or > 220)
        {
            return false;
        }

        var withText = parsed.Questions.Count(q =>
            !string.IsNullOrWhiteSpace(q.Text) &&
            q.Text.Any(char.IsLetter) &&
            q.Text.Length >= 8);

        var yesNoLike = parsed.Questions.Count(q =>
            q.Type is QuestionType.YesNo or QuestionType.SingleChoice);

        return withText >= 180 && yesNoLike >= 180;
    }

    private static string? ResolveAdaptivity200SeedDocx(string sourcePath)
    {
        var contentRoot = TryFindContentRoot(sourcePath);
        var candidates = new List<string>();
        if (contentRoot is not null)
        {
            candidates.Add(Path.Combine(contentRoot, "SeedDocuments", "Адаптивність-200.docx"));
            candidates.Add(Path.Combine(contentRoot, "SeedDocuments", "adaptivity-200.docx"));
            candidates.Add(Path.Combine(contentRoot, "App_Data", "Documents", "Адаптивність-200", "Адаптивність-200.docx"));
        }

        // Поруч із завантаженим .doc
        var dir = Path.GetDirectoryName(sourcePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            candidates.Add(Path.Combine(dir, "Адаптивність-200.docx"));
            candidates.Add(Path.Combine(dir, "adaptivity-200.docx"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? TryFindContentRoot(string path)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)) ?? path);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BlazorLppp.csproj")) ||
                Directory.Exists(Path.Combine(dir.FullName, "SeedDocuments")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool LooksLikeIncompleteZbroya(ParsedTestDocument parsed)
    {
        var looksLikeZbroya =
            IsZbroyaTitle(parsed.Title) ||
            (parsed.Instruction?.Contains("зі зброєю", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (parsed.Instruction?.Contains("ПОЧУВАЄТЕСЯ ЦІЄЇ МИТІ", StringComparison.OrdinalIgnoreCase) ?? false) ||
            parsed.Questions.Any(q => ZbroyaDocumentTemplate.IsKnownReactiveItem(q.Text));

        if (!looksLikeZbroya)
        {
            return false;
        }

        var validReactive = parsed.Questions.Count(q =>
            q.SortOrder is >= 1 and <= 20 &&
            q.Text.Length >= 8 &&
            q.Text.Any(char.IsLetter));

        return validReactive < 20 || parsed.Questions.Count < 24;
    }

    private static void EnableZbroyaMode(ParsedTestDocument result, ref bool isZbroya)
    {
        isZbroya = true;
        result.Title = "Тест ЗБРОЯ (готовність до служби зі зброєю)";
        result.Instruction ??=
            "Прочитайте уважно кожне речення і оберіть оцінку залежно від того, як ви почуваєтеся цієї миті. " +
            "1 — Ні, це не так; 2 — Мабуть так; 3 — Правильно; 4 — Абсолютно правильно.";
    }

    private static bool LooksLikeZbroyaContentLine(string line)
    {
        var questionMatch = NumberedQuestion().Match(line);
        if (questionMatch.Success)
        {
            return ZbroyaDocumentTemplate.IsKnownReactiveItem(questionMatch.Groups[2].Value.Trim());
        }

        return ZbroyaDocumentTemplate.IsKnownReactiveItem(line);
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path) &&
                path.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignored
        }
    }

    internal static ParsedTestDocument ParseLines(
        IReadOnlyList<string> rawLines,
        bool forceAssinger = false,
        bool forceNpna = false)
    {
        var lines = rawLines
            .Select(NormalizeLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var result = new ParsedTestDocument();
        ParsedTestQuestion? current = null;
        QuestionType? defaultType = null;
        int? pendingNumber = null;
        var seenYes = false;
        var seenNo = false;
        var isAdaptivity200 = false;
        var isZbroya = false;
        var isHorska = false;
        var isAssinger = forceAssinger;
        var isNpna = forceNpna;

        TryAssignTitleFromDocument(lines, result);
        if (IsAdaptivity200Title(result.Title) || lines.Any(IsAdaptivity200Title))
        {
            isAdaptivity200 = true;
            result.Title = "Адаптивність-200 (БОО)";
            defaultType = QuestionType.YesNo;
            result.Instruction ??=
                "Вам пропонуються твердження. Якщо твердження відповідає Вам — оберіть «Так», якщо ні — «Ні». " +
                "Над відповідями довго не замислюйтеся; правильних або неправильних відповідей немає.";
        }
        else if (isNpna ||
                 IsNpnaTitle(result.Title) ||
                 lines.Any(IsNpnaTitle))
        {
            EnableNpnaMode(result, ref isNpna, ref defaultType);
        }
        else if (IsZbroyaTitle(result.Title) ||
                 lines.Any(IsZbroyaTitle) ||
                 lines.Any(IsZbroyaInstructionLine) ||
                 lines.Any(LooksLikeZbroyaContentLine))
        {
            isZbroya = true;
            result.Title = "Тест ЗБРОЯ (готовність до служби зі зброєю)";
            result.Instruction ??=
                "Прочитайте уважно кожне речення і оберіть оцінку залежно від того, як ви почуваєтеся цієї миті. " +
                "1 — Ні, це не так; 2 — Мабуть так; 3 — Правильно; 4 — Абсолютно правильно.";
        }
        else if (IsHorskaTitle(result.Title) || lines.Any(IsHorskaTitle))
        {
            isHorska = true;
            result.Title = "Методика вивчення схильності до суїцидальної поведінки (М.В. Горська)";
            defaultType = QuestionType.Scale;
            result.Instruction ??=
                "Проти кожного твердження поставте оцінку за таким принципом: якщо твердження вам підходить — ставте оцінку 2, " +
                "якщо не зовсім підходить — ставте оцінку 1, якщо зовсім не підходить — ставте 0.";
        }
        else if (isAssinger ||
                 IsAssingerTitle(result.Title) ||
                 lines.Any(IsAssingerTitle) ||
                 lines.Any(LooksLikeAssingerQuestionLine))
        {
            EnableAssingerMode(result, ref isAssinger);
        }

        foreach (var line in lines)
        {
            if (result.Questions.Count > 0 && IsEndOfQuestionsSection(line))
            {
                break;
            }

            if (isAdaptivity200 && result.Questions.Count >= 200)
            {
                break;
            }

            if (isZbroya && result.Questions.Count >= 24)
            {
                break;
            }

            if (isHorska && result.Questions.Count >= 40)
            {
                break;
            }

            if (isNpna && result.Questions.Count >= NpnaDocumentTemplate.QuestionCount)
            {
                break;
            }

            if (IsAssingerInstructionLine(line))
            {
                result.Instruction = AssingerDocumentTemplate.CanonicalInstruction;
                continue;
            }

            if (isAssinger && IsAssingerNoiseLine(line))
            {
                continue;
            }

            if (isNpna && IsNpnaInstructionLine(line))
            {
                result.Instruction = NpnaDocumentTemplate.CanonicalInstruction;
                continue;
            }

            if (isNpna && IsNpnaNoiseLine(line))
            {
                continue;
            }

            if (IsHorskaInstructionLine(line))
            {
                result.Instruction = CleanHorskaInstruction(line);
                continue;
            }

            if (IsZbroyaInstructionLine(line))
            {
                if (string.IsNullOrWhiteSpace(result.Instruction) ||
                    result.Instruction.Length < line.Length)
                {
                    result.Instruction = CleanZbroyaInstruction(line);
                }

                continue;
            }

            if (IsMetadataLine(line))
            {
                if (result.Questions.Count > 0 && IsPersonalHeaderLine(line))
                {
                    break;
                }

                if (line.StartsWith("Інструкція", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Инструкция", StringComparison.OrdinalIgnoreCase))
                {
                    var instruction = InstructionPrefix().Replace(line, string.Empty).Trim();
                    if (!isNpna && !string.IsNullOrWhiteSpace(instruction) && instruction.Length >= 40)
                    {
                        result.Instruction = instruction;
                    }
                }

                continue;
            }

            if (isZbroya && IsZbroyaNoiseLine(line))
            {
                continue;
            }

            if (IsYesNoHeader(line))
            {
                if (line.Equals("Так", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                {
                    seenYes = true;
                }
                else
                {
                    seenNo = true;
                }

                if (seenYes && seenNo)
                {
                    defaultType = QuestionType.YesNo;
                }

                continue;
            }

            if (IsTitleCandidate(line) && result.Questions.Count == 0 && current is null && pendingNumber is null)
            {
                if (isZbroya)
                {
                    result.Title = "Тест ЗБРОЯ (готовність до служби зі зброєю)";
                }
                else if (isHorska)
                {
                    result.Title = "Методика вивчення схильності до суїцидальної поведінки (М.В. Горська)";
                }
                else if (isAdaptivity200)
                {
                    result.Title = "Адаптивність-200 (БОО)";
                }
                else if (isAssinger)
                {
                    result.Title = AssingerDocumentTemplate.CanonicalTitle;
                }
                else if (isNpna)
                {
                    result.Title = NpnaDocumentTemplate.CanonicalTitle;
                }
                else
                {
                    result.Title = CleanTitle(line);
                }

                continue;
            }

            if (isAssinger && current is not null)
            {
                var assingerOption = AssingerOption().Match(line);
                if (assingerOption.Success)
                {
                    AddAssingerOption(
                        current,
                        int.Parse(assingerOption.Groups[1].Value),
                        assingerOption.Groups[2].Value.Trim());
                    pendingNumber = null;
                    continue;
                }
            }

            var romanQuestion = RomanQuestion().Match(line);
            if (romanQuestion.Success)
            {
                if (!isAssinger && !isAdaptivity200 && !isZbroya && !isHorska && !isNpna)
                {
                    EnableAssingerMode(result, ref isAssinger);
                }

                if (isAssinger)
                {
                    var sortOrder = RomanToInt(romanQuestion.Groups[1].Value);
                    if (sortOrder is < 1 or > 20)
                    {
                        continue;
                    }

                    var romanText = romanQuestion.Groups[2].Value.Trim();
                    pendingNumber = null;
                    current = CreateQuestion(sortOrder, romanText, QuestionType.SingleChoice);
                    result.Questions.Add(current);
                    continue;
                }
            }

            var numberOnly = NumberOnlyQuestion().Match(line);
            if (numberOnly.Success)
            {
                var bareNumber = int.Parse(numberOnly.Groups[1].Value);

                // У тесті ЗБРОЯ після питання йдуть клітинки відповідей 1..4
                if (isZbroya && current is not null && bareNumber is >= 1 and <= 4 && current.SortOrder <= 20)
                {
                    EnsureZbroyaNumericOption(current, bareNumber);
                    pendingNumber = null;
                    continue;
                }

                // У бланку відповідей Адаптивності ідуть лише номери підряд без тексту питань.
                if (!isZbroya && pendingNumber.HasValue && result.Questions.Count > 0)
                {
                    break;
                }

                pendingNumber = bareNumber;
                current = null;
                continue;
            }

            if (isZbroya && TryParseZbroyaExtraSection(line, result, ref current))
            {
                pendingNumber = null;
                continue;
            }

            var questionMatch = NumberedQuestion().Match(line);
            if (questionMatch.Success)
            {
                var sortOrder = int.Parse(questionMatch.Groups[1].Value);
                var text = questionMatch.Groups[2].Value.Trim();

                if (isAssinger && current is not null && sortOrder is >= 1 and <= 3)
                {
                    AddAssingerOption(current, sortOrder, text);
                    pendingNumber = null;
                    continue;
                }

                // Увімкнути режим ЗБРОЯ щойно з’явилось перше відоме твердження —
                // інакше клітинки 1..4 після Q1 обривають розбір.
                if (!isZbroya && !isAdaptivity200 && !isHorska && !isNpna && !isAssinger &&
                    ZbroyaDocumentTemplate.IsKnownReactiveItem(text))
                {
                    EnableZbroyaMode(result, ref isZbroya);
                }

                // Друга копія бланка / службові пункти після 20 питань
                if (isZbroya && result.Questions.Any(q => q.SortOrder == sortOrder))
                {
                    if (TryParseZbroyaExtraSection(line, result, ref current))
                    {
                        pendingNumber = null;
                    }

                    continue;
                }

                pendingNumber = null;
                current = CreateQuestion(sortOrder, text, defaultType);
                if (isHorska)
                {
                    ApplyHorskaScale(current);
                }

                result.Questions.Add(current);
                continue;
            }

            if (pendingNumber.HasValue)
            {
                if (!isZbroya && !isAdaptivity200 && !isHorska && !isNpna && !isAssinger &&
                    ZbroyaDocumentTemplate.IsKnownReactiveItem(line))
                {
                    EnableZbroyaMode(result, ref isZbroya);
                }

                current = CreateQuestion(pendingNumber.Value, line, defaultType);
                if (isHorska)
                {
                    ApplyHorskaScale(current);
                }

                result.Questions.Add(current);
                pendingNumber = null;
                continue;
            }

            if (current is null)
            {
                if (isHorska && IsHorskaStatementLine(line))
                {
                    current = CreateQuestion(result.Questions.Count + 1, line, QuestionType.Scale);
                    ApplyHorskaScale(current);
                    result.Questions.Add(current);
                }

                continue;
            }

            if (isHorska && IsHorskaStatementLine(line) &&
                !LetterOption().IsMatch(line) &&
                !YesNoOption().IsMatch(line) &&
                !ScaleHint().IsMatch(line))
            {
                current = CreateQuestion(result.Questions.Count + 1, line, QuestionType.Scale);
                ApplyHorskaScale(current);
                result.Questions.Add(current);
                continue;
            }

            // У бланку НПН-А питання 191 без номера — наступне твердження після 190.
            if (isNpna &&
                current is not null &&
                !string.IsNullOrWhiteSpace(current.Text) &&
                LooksLikeNpnaStatement(line) &&
                result.Questions.Count < NpnaDocumentTemplate.QuestionCount)
            {
                current = CreateQuestion(result.Questions.Count + 1, line, QuestionType.YesNo);
                result.Questions.Add(current);
                pendingNumber = null;
                continue;
            }

            var letterOption = LetterOption().Match(line);
            if (letterOption.Success)
            {
                current.Type = QuestionType.SingleChoice;
                current.Options.Add(new ParsedTestOption
                {
                    SortOrder = current.Options.Count + 1,
                    Key = letterOption.Groups[1].Value.ToUpperInvariant(),
                    Text = letterOption.Groups[2].Value.Trim()
                });
                continue;
            }

            var yesNoMatches = YesNoOption().Matches(line);
            if (yesNoMatches.Count > 0)
            {
                current.Type = QuestionType.YesNo;
                foreach (Match match in yesNoMatches)
                {
                    var value = match.Groups["opt"].Value.Trim();
                    if (current.Options.Any(o =>
                            o.Text.Equals(value, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    current.Options.Add(new ParsedTestOption
                    {
                        SortOrder = current.Options.Count + 1,
                        Key = value.StartsWith("Т", StringComparison.OrdinalIgnoreCase) ||
                              value.StartsWith("Y", StringComparison.OrdinalIgnoreCase)
                            ? "Так"
                            : "Ні",
                        Text = value
                    });
                }

                continue;
            }

            var scaleHint = ScaleHint().Match(line);
            if (scaleHint.Success)
            {
                current.Type = QuestionType.Scale;
                current.ScaleMin = int.Parse(scaleHint.Groups[1].Value);
                current.ScaleMax = int.Parse(scaleHint.Groups[2].Value);
                current.Hint = line.Trim();
                continue;
            }

            if (ScaleNumbers().IsMatch(line) || AnswerBlank().IsMatch(line))
            {
                if (current.Type == default)
                {
                    current.Type = QuestionType.Scale;
                    current.ScaleMin ??= 1;
                    current.ScaleMax ??= 10;
                }

                continue;
            }

            if (isAssinger)
            {
                continue;
            }

            current.Text = $"{current.Text} {line}".Trim();
        }

        FinalizeQuestions(result, defaultType, isHorska);
        if (isZbroya)
        {
            FinalizeZbroyaQuestions(result);
        }

        if (isAssinger)
        {
            FinalizeAssingerQuestions(result);
        }

        if (isNpna)
        {
            FinalizeNpnaQuestions(result);
        }

        if (result.Questions.Count == 0)
        {
            if (isAssinger)
            {
                return AssingerDocumentTemplate.Create();
            }

            if (isNpna)
            {
                return NpnaDocumentTemplate.Create();
            }

            throw new InvalidOperationException(
                "У документі не знайдено питань. Очікується нумерація на кшталт «1. Текст питання?».");
        }

        return result;
    }

    private static void FinalizeZbroyaQuestions(ParsedTestDocument result)
    {
        EnsureZbroyaSanAndReadiness(result);

        foreach (var question in result.Questions.OrderBy(q => q.SortOrder))
        {
            if (question.SortOrder is >= 1 and <= 20)
            {
                question.Type = QuestionType.SingleChoice;
                if (question.Options.Count == 0)
                {
                    AddDefaultZbroyaOptions(question);
                }
                else
                {
                    NormalizeZbroyaOptions(question);
                }

                continue;
            }

            if (question.SortOrder is >= 21 and <= 23)
            {
                question.Type = QuestionType.Scale;
                question.ScaleMin = 0;
                question.ScaleMax = 10;
                question.Hint ??= "0 — 0%, 10 — 100%";
                question.Options.Clear();
                continue;
            }

            if (question.SortOrder == 24)
            {
                question.Type = QuestionType.YesNo;
                question.ScaleMin = null;
                question.ScaleMax = null;
                question.Options.Clear();
                question.Options.Add(new ParsedTestOption { SortOrder = 1, Key = "Так", Text = "Так" });
                question.Options.Add(new ParsedTestOption { SortOrder = 2, Key = "Ні", Text = "Ні" });
            }
        }
    }

    private static void EnsureZbroyaSanAndReadiness(ParsedTestDocument result)
    {
        if (!result.Questions.Any(q => q.SortOrder == 21))
        {
            result.Questions.Add(CreateQuestion(21, "САМОПОЧУТТЯ", QuestionType.Scale));
        }

        if (!result.Questions.Any(q => q.SortOrder == 22))
        {
            result.Questions.Add(CreateQuestion(22, "АКТИВНІСТЬ", QuestionType.Scale));
        }

        if (!result.Questions.Any(q => q.SortOrder == 23))
        {
            result.Questions.Add(CreateQuestion(23, "НАСТРІЙ", QuestionType.Scale));
        }

        if (!result.Questions.Any(q => q.SortOrder == 24))
        {
            result.Questions.Add(CreateQuestion(
                24,
                "Чи згодні Ви із зазначеним висловом: «Я маю необхідні знання і практичні навички, вивчив функціональні обов’язки, пройшов відповідний інструктаж, його вимоги мені зрозумілі, проблемних питань щодо організації несення служби не маю. Мій стан здоров’я, настрій, самопочуття і активність, морально-психологічний стан дозволяють мені виконувати службові обов’язки із зброєю. Проблемних питань, негативних чинників впливу на мій морально-психологічний стан не маю. Готовий нести службу зі зброєю»?",
                QuestionType.YesNo));
        }
    }

    private static bool TryParseZbroyaExtraSection(
        string line,
        ParsedTestDocument result,
        ref ParsedTestQuestion? current)
    {
        if (ContainsIgnoreCase(line, "САМОПОЧУТТЯ") && result.Questions.Count >= 20)
        {
            current = UpsertZbroyaQuestion(result, 21, "САМОПОЧУТТЯ", QuestionType.Scale);
            return true;
        }

        if (line.Equals("АКТИВНІСТЬ", StringComparison.OrdinalIgnoreCase) ||
            (ContainsIgnoreCase(line, "АКТИВНІСТЬ") && result.Questions.Count >= 20))
        {
            current = UpsertZbroyaQuestion(result, 22, "АКТИВНІСТЬ", QuestionType.Scale);
            return true;
        }

        if (line.Equals("НАСТРІЙ", StringComparison.OrdinalIgnoreCase) ||
            (ContainsIgnoreCase(line, "НАСТРІЙ") && result.Questions.Count >= 20 && !ContainsIgnoreCase(line, "самопочуття")))
        {
            current = UpsertZbroyaQuestion(result, 23, "НАСТРІЙ", QuestionType.Scale);
            return true;
        }

        if (ContainsIgnoreCase(line, "Чи згодні Ви") ||
            ContainsIgnoreCase(line, "Готовий нести службу зі зброєю"))
        {
            // У бланку декларація розбита на кілька абзаців — підставляємо повний текст.
            const string readinessText =
                "Чи згодні Ви із зазначеним висловом: «Я маю необхідні знання і практичні навички, вивчив функціональні обов’язки, " +
                "пройшов відповідний інструктаж, його вимоги мені зрозумілі, проблемних питань щодо організації несення служби не маю. " +
                "Мій стан здоров’я, настрій, самопочуття і активність, морально-психологічний стан дозволяють мені виконувати службові обов’язки із зброєю. " +
                "Проблемних питань, негативних чинників впливу на мій морально-психологічний стан не маю. Готовий нести службу зі зброєю»?";
            current = UpsertZbroyaQuestion(result, 24, readinessText, QuestionType.YesNo);
            return true;
        }

        return false;
    }

    private static ParsedTestQuestion UpsertZbroyaQuestion(
        ParsedTestDocument result,
        int sortOrder,
        string text,
        QuestionType type)
    {
        var existing = result.Questions.FirstOrDefault(q => q.SortOrder == sortOrder);
        if (existing is not null)
        {
            if (existing.Text.Length < text.Length)
            {
                existing.Text = text;
            }

            existing.Type = type;
            return existing;
        }

        var question = CreateQuestion(sortOrder, text, type);
        result.Questions.Add(question);
        return question;
    }

    private static void EnsureZbroyaNumericOption(ParsedTestQuestion question, int value)
    {
        var key = value.ToString();
        if (question.Options.Any(o => o.Key == key))
        {
            return;
        }

        question.Type = QuestionType.SingleChoice;
        question.Options.Add(new ParsedTestOption
        {
            SortOrder = value,
            Key = key,
            Text = ZbroyaOptionLabel(value)
        });
    }

    private static void AddDefaultZbroyaOptions(ParsedTestQuestion question)
    {
        for (var value = 1; value <= 4; value++)
        {
            question.Options.Add(new ParsedTestOption
            {
                SortOrder = value,
                Key = value.ToString(),
                Text = ZbroyaOptionLabel(value)
            });
        }
    }

    private static void NormalizeZbroyaOptions(ParsedTestQuestion question)
    {
        var ordered = question.Options.OrderBy(o => o.SortOrder).ToList();
        question.Options.Clear();
        for (var i = 0; i < ordered.Count && i < 4; i++)
        {
            var value = i + 1;
            question.Options.Add(new ParsedTestOption
            {
                SortOrder = value,
                Key = value.ToString(),
                Text = string.IsNullOrWhiteSpace(ordered[i].Text) || ordered[i].Text.Length <= 2
                    ? ZbroyaOptionLabel(value)
                    : ordered[i].Text
            });
        }

        if (question.Options.Count < 4)
        {
            AddDefaultZbroyaOptions(question);
            question.Options = question.Options
                .GroupBy(o => o.Key)
                .Select(g => g.First())
                .OrderBy(o => o.SortOrder)
                .ToList();
        }
    }

    private static string ZbroyaOptionLabel(int value) => value switch
    {
        1 => "Ні, це не так",
        2 => "Мабуть так",
        3 => "Правильно",
        4 => "Абсолютно правильно",
        _ => value.ToString()
    };

    private static bool IsZbroyaNoiseLine(string line)
        => line.Equals("Ні, це не так", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Мабуть так", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Мабуть", StringComparison.OrdinalIgnoreCase)
           || line.Equals("так", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Правильно", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Абсолютно правильно", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Речення", StringComparison.OrdinalIgnoreCase)
           || line.Equals("0%", StringComparison.OrdinalIgnoreCase)
           || line.Equals("50%", StringComparison.OrdinalIgnoreCase)
           || line.Equals("100%", StringComparison.OrdinalIgnoreCase)
           || line.Equals("50", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Шановний", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Пропонуємо Вам перевірити", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Ваші правдиві", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Поставте позначку на шкалі", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("2. Поставте позначку", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Якщо згодні", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Якщо ні", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Напишіть про Ваші пропозиції", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("4. Напишіть про Ваші пропозиції", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Обстеження провів", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsIgnoreCase(string? source, string value)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static void ApplyHorskaScale(ParsedTestQuestion question)
    {
        question.Type = QuestionType.Scale;
        question.ScaleMin = 0;
        question.ScaleMax = 2;
        question.Hint = "0 — не підходить; 1 — не зовсім підходить; 2 — підходить";
        question.Options.Clear();
    }

    private static ParsedTestQuestion CreateQuestion(int sortOrder, string text, QuestionType? defaultType)
        => new()
        {
            SortOrder = sortOrder,
            Text = text,
            Type = defaultType ?? default
        };

    private static void FinalizeQuestions(ParsedTestDocument result, QuestionType? defaultType, bool isHorska)
    {
        foreach (var question in result.Questions)
        {
            if (isHorska)
            {
                ApplyHorskaScale(question);
                continue;
            }

            if (question.Type == default && defaultType.HasValue)
            {
                question.Type = defaultType.Value;
            }

            if (question.Type == QuestionType.YesNo && question.Options.Count == 0)
            {
                question.Options.Add(new ParsedTestOption { SortOrder = 1, Key = "Так", Text = "Так" });
                question.Options.Add(new ParsedTestOption { SortOrder = 2, Key = "Ні", Text = "Ні" });
            }

            if (question.Type == QuestionType.Scale)
            {
                question.ScaleMin ??= 1;
                question.ScaleMax ??= 10;
                question.Options.Clear();
            }

            if (question.Type == default)
            {
                if (question.Options.Count > 0)
                {
                    question.Type = question.Options.All(o =>
                        o.Text.Equals("Так", StringComparison.OrdinalIgnoreCase) ||
                        o.Text.Equals("Ні", StringComparison.OrdinalIgnoreCase) ||
                        o.Text.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                        o.Text.Equals("No", StringComparison.OrdinalIgnoreCase))
                        ? QuestionType.YesNo
                        : QuestionType.SingleChoice;
                }
                else
                {
                    question.Type = QuestionType.Scale;
                    question.ScaleMin ??= 1;
                    question.ScaleMax ??= 10;
                }
            }

            if (question.Type == QuestionType.YesNo && question.Options.Count == 0)
            {
                question.Options.Add(new ParsedTestOption { SortOrder = 1, Key = "Так", Text = "Так" });
                question.Options.Add(new ParsedTestOption { SortOrder = 2, Key = "Ні", Text = "Ні" });
            }
        }
    }

    private static void TryAssignTitleFromDocument(IReadOnlyList<string> lines, ParsedTestDocument result)
    {
        foreach (var line in lines)
        {
            if (IsAdaptivity200Title(line))
            {
                result.Title = "Адаптивність-200 (БОО)";
                return;
            }

            if (IsZbroyaTitle(line))
            {
                result.Title = "Тест ЗБРОЯ (готовність до служби зі зброєю)";
                return;
            }

            if (IsHorskaTitle(line))
            {
                result.Title = "Методика вивчення схильності до суїцидальної поведінки (М.В. Горська)";
                return;
            }

            if (IsAssingerTitle(line))
            {
                result.Title = AssingerDocumentTemplate.CanonicalTitle;
                return;
            }

            if (IsNpnaTitle(line))
            {
                result.Title = NpnaDocumentTemplate.CanonicalTitle;
                return;
            }

            if (line.Contains("Методика виявлення схильності до суїцидальних", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Методика выявления склонности к суицидальным", StringComparison.OrdinalIgnoreCase))
            {
                result.Title = "ТЕСТ суїцид (СР-45 / СР-10)";
                return;
            }

            if (line.Contains("Методика виявлення", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Методика выявления", StringComparison.OrdinalIgnoreCase))
            {
                result.Title = CleanTitle(line);
                return;
            }
        }
    }

    internal static bool IsAdaptivity200Title(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("АДАПТИВІСТЬ-200", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Адаптивність-200", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Адаптивність 200", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("АДАПТИВНІСТЬ-200", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("АДАПТИВНІСТЬ 200", StringComparison.OrdinalIgnoreCase) ||
            (value.Contains("БОО", StringComparison.OrdinalIgnoreCase) &&
             value.Contains("Адаптив", StringComparison.OrdinalIgnoreCase)));

    internal static bool IsZbroyaTitle(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("ЗБРОЯ", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("зі зброєю", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("готовність до несення служби зі зброєю", StringComparison.OrdinalIgnoreCase));

    private static bool IsZbroyaInstructionLine(string line)
        => line.Contains("ЯК ВИ ПОЧУВАЄТЕСЯ", StringComparison.OrdinalIgnoreCase) ||
           (line.Contains("Прочитайте уважно кожне", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("речен", StringComparison.OrdinalIgnoreCase));

    private static string CleanZbroyaInstruction(string line)
    {
        var cleaned = InstructionPrefix().Replace(line, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? line.Trim() : cleaned;
    }

    internal static bool IsHorskaTitle(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("Горськ", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("М.В. Горська", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("вивчення схильності до суїцидальної поведінки", StringComparison.OrdinalIgnoreCase));

    internal static bool IsAnonymousSurveyFileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return name.Contains("анонімне", StringComparison.OrdinalIgnoreCase)
               || name.Contains("анонимн", StringComparison.OrdinalIgnoreCase)
               || name.Contains("anonymous", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAnonymousSurveyTitle(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("Анонімне опитуван", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("анонімне анкетуван", StringComparison.OrdinalIgnoreCase));

    internal static bool IsAssingerFileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return name.Contains("агресивн", StringComparison.OrdinalIgnoreCase)
               || name.Contains("ассингер", StringComparison.OrdinalIgnoreCase)
               || name.Contains("assinger", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAssingerTitle(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("Ассингер", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Assinger", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("оцінка агресивності", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("агресивності у відношен", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("агресивності у відносинах", StringComparison.OrdinalIgnoreCase));

    private static bool IsCompleteAssingerDocument(ParsedTestDocument parsed)
        => parsed.Questions.Count == 20 &&
           parsed.Questions.All(q =>
               q.Type == QuestionType.SingleChoice &&
               q.Options.Count >= 3 &&
               !string.IsNullOrWhiteSpace(q.Text) &&
               q.Text.Any(char.IsLetter));

    private static void EnableAssingerMode(ParsedTestDocument result, ref bool isAssinger)
    {
        isAssinger = true;
        result.Title = AssingerDocumentTemplate.CanonicalTitle;
        result.Instruction ??= AssingerDocumentTemplate.CanonicalInstruction;
    }

    private static bool LooksLikeAssingerQuestionLine(string line)
        => RomanQuestion().IsMatch(line);

    internal static bool IsNpnaFileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var compact = name.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
        return compact.Contains("нпна", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("npna", StringComparison.OrdinalIgnoreCase)
               || name.Contains("нпн", StringComparison.OrdinalIgnoreCase)
               || name.Contains("npn", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsNpnaTitle(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("НПН-А", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("НПН А", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("НПНА", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("NPN-A", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("NPNA", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("нервово-психічн", StringComparison.OrdinalIgnoreCase) ||
            (value.Contains("НПН", StringComparison.OrdinalIgnoreCase) &&
             (value.Contains("опитувальник", StringComparison.OrdinalIgnoreCase) ||
              value.Contains("акцентуац", StringComparison.OrdinalIgnoreCase))));

    private static bool IsCompleteNpnaDocument(ParsedTestDocument parsed)
        => parsed.Questions.Count == NpnaDocumentTemplate.QuestionCount &&
           parsed.Questions.All(q =>
               q.Type is QuestionType.YesNo or QuestionType.SingleChoice &&
               !string.IsNullOrWhiteSpace(q.Text) &&
               q.Text.Any(char.IsLetter) &&
               q.Text.Length >= 8);

    private static void EnableNpnaMode(ParsedTestDocument result, ref bool isNpna, ref QuestionType? defaultType)
    {
        isNpna = true;
        defaultType = QuestionType.YesNo;
        result.Title = NpnaDocumentTemplate.CanonicalTitle;
        result.Instruction = NpnaDocumentTemplate.CanonicalInstruction;
    }

    private static bool IsNpnaInstructionLine(string line)
        => line.Contains("немає правильних або неправильних відповідей", StringComparison.OrdinalIgnoreCase) ||
           (line.StartsWith("Зараз Вам буде запропоновано", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("самопочуття", StringComparison.OrdinalIgnoreCase));

    private static bool IsNpnaNoiseLine(string line)
        => line.Equals("Текст опитувальника", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Запитання", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Відповідь", StringComparison.OrdinalIgnoreCase)
           || line.Equals("+", StringComparison.Ordinal)
           || line.Equals("−", StringComparison.Ordinal)
           || line.Equals("–", StringComparison.Ordinal)
           || line.Equals("+/-", StringComparison.OrdinalIgnoreCase)
           || line.Equals("+/−", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Опитувальник призначений", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Опитувальник містить", StringComparison.OrdinalIgnoreCase)
           || line.Equals("достовірності,", StringComparison.OrdinalIgnoreCase)
           || line.Equals("нервово-психічної нестійкості,", StringComparison.OrdinalIgnoreCase)
           || line.Equals("істерії,", StringComparison.OrdinalIgnoreCase)
           || line.Equals("психастенії,", StringComparison.OrdinalIgnoreCase)
           || line.Equals("психопатії,", StringComparison.OrdinalIgnoreCase)
           || line.Equals("параної,", StringComparison.OrdinalIgnoreCase)
           || line.Equals("шизофренії.", StringComparison.OrdinalIgnoreCase)
           || line.Equals("шизофренії", StringComparison.OrdinalIgnoreCase)
           || (line.StartsWith("(нервово-психічна", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeNpnaStatement(string line)
        => line.Length >= 20 &&
           char.IsLetter(line[0]) &&
           !IsEndOfQuestionsSection(line) &&
           !IsMetadataLine(line) &&
           !IsNpnaNoiseLine(line) &&
           !NumberOnlyQuestion().IsMatch(line) &&
           !NumberedQuestion().IsMatch(line);

    private static void FinalizeNpnaQuestions(ParsedTestDocument result)
    {
        result.Title = NpnaDocumentTemplate.CanonicalTitle;
        result.Instruction = NpnaDocumentTemplate.CanonicalInstruction;

        foreach (var question in result.Questions.OrderBy(q => q.SortOrder))
        {
            question.Type = QuestionType.YesNo;
            question.ScaleMin = null;
            question.ScaleMax = null;
            question.Hint = null;
            if (question.Options.Count == 0 ||
                question.Options.All(o =>
                    !o.Text.Equals("Так", StringComparison.OrdinalIgnoreCase) &&
                    !o.Key.Equals("Так", StringComparison.OrdinalIgnoreCase)))
            {
                question.Options.Clear();
                question.Options.Add(new ParsedTestOption { SortOrder = 1, Key = "Так", Text = "Так" });
                question.Options.Add(new ParsedTestOption { SortOrder = 2, Key = "Ні", Text = "Ні" });
            }
        }
    }

    private static bool IsAssingerInstructionLine(string line)
        => line.StartsWith("Виберіть одну з відповідей", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssingerNoiseLine(string line)
        => line.Equals("Текст опитувальника", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Флегматична людина", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Тест А.Ассингера дозволяє", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Тест А. Ассингера дозволяє", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Для більшої об'єктивності", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Для більшої об’єктивності", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Уважно проглянете", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("45 і більше", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("36 - 44", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("36-44", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("35 і менше", StringComparison.OrdinalIgnoreCase);

    private static void AddAssingerOption(ParsedTestQuestion question, int number, string text)
    {
        if (number is < 1 or > 3 || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        question.Type = QuestionType.SingleChoice;
        var key = number.ToString();
        var existing = question.Options.FirstOrDefault(o => o.Key == key || o.SortOrder == number);
        if (existing is not null)
        {
            if (existing.Text.Length < text.Length)
            {
                existing.Text = text;
            }

            return;
        }

        question.Options.Add(new ParsedTestOption
        {
            SortOrder = number,
            Key = key,
            Text = text
        });
    }

    private static void FinalizeAssingerQuestions(ParsedTestDocument result)
    {
        var template = AssingerDocumentTemplate.Create();
        foreach (var question in result.Questions.OrderBy(q => q.SortOrder))
        {
            question.Type = QuestionType.SingleChoice;
            question.ScaleMin = null;
            question.ScaleMax = null;

            var ordered = question.Options
                .OrderBy(o => o.SortOrder)
                .Take(3)
                .ToList();
            question.Options.Clear();
            for (var i = 0; i < ordered.Count; i++)
            {
                var number = i + 1;
                question.Options.Add(new ParsedTestOption
                {
                    SortOrder = number,
                    Key = number.ToString(),
                    Text = ordered[i].Text
                });
            }

            if (question.Options.Count >= 3)
            {
                continue;
            }

            var templateQuestion = template.Questions.FirstOrDefault(q => q.SortOrder == question.SortOrder);
            if (templateQuestion is null)
            {
                continue;
            }

            question.Options.Clear();
            foreach (var option in templateQuestion.Options)
            {
                question.Options.Add(new ParsedTestOption
                {
                    SortOrder = option.SortOrder,
                    Key = option.Key,
                    Text = option.Text
                });
            }

            if (string.IsNullOrWhiteSpace(question.Text) || question.Text.Length < templateQuestion.Text.Length / 2)
            {
                question.Text = templateQuestion.Text;
            }
        }
    }

    private static int RomanToInt(string roman)
    {
        var normalized = roman.Trim()
            .Replace('\u0406', 'I')
            .Replace('\u0456', 'I')
            .Replace('\u0425', 'X')
            .Replace('\u0445', 'X')
            .ToUpperInvariant();

        return normalized switch
        {
            "I" => 1,
            "II" => 2,
            "III" => 3,
            "IV" => 4,
            "V" => 5,
            "VI" => 6,
            "VII" => 7,
            "VIII" => 8,
            "IX" => 9,
            "X" => 10,
            "XI" => 11,
            "XII" => 12,
            "XIII" => 13,
            "XIV" => 14,
            "XV" => 15,
            "XVI" => 16,
            "XVII" => 17,
            "XVIII" => 18,
            "XIX" => 19,
            "XX" => 20,
            _ => 0
        };
    }

    private static bool IsHorskaInstructionLine(string line)
        => line.Contains("якщо твердження вам підходить", StringComparison.OrdinalIgnoreCase) ||
           line.Contains("ставте оцінку", StringComparison.OrdinalIgnoreCase);

    private static string CleanHorskaInstruction(string line)
    {
        var cleaned = InstructionPrefix().Replace(line, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? line.Trim() : cleaned;
    }

    private static bool IsHorskaStatementLine(string line)
        => line.Length >= 12 &&
           !IsMetadataLine(line) &&
           !IsEndOfQuestionsSection(line) &&
           !IsYesNoHeader(line) &&
           !IsHorskaTitle(line) &&
           !line.StartsWith("Бланк", StringComparison.OrdinalIgnoreCase) &&
           !NumberOnlyQuestion().IsMatch(line);


    private static IReadOnlyList<string> ReadTxtLines(string filePath)
        => File.ReadAllLines(filePath, Encoding.UTF8);

    private static IReadOnlyList<string> ReadDocxLines(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false);
        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("Документ Word не містить тексту.");

        var lines = new List<string>();
        foreach (var child in body.ChildElements)
        {
            if (child is Table table)
            {
                AppendTableLines(table, lines);
                continue;
            }

            if (child is Paragraph paragraph)
            {
                AppendParagraphLine(paragraph, lines);
            }
        }

        // Фолбек для вкладених таблиць / нестандартної розмітки.
        if (lines.Count == 0)
        {
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                AppendParagraphLine(paragraph, lines);
            }
        }

        return lines;
    }

    private static void AppendTableLines(Table table, List<string> lines)
    {
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>()
                .Select(GetCellText)
                .ToList();

            if (cells.Count == 0 || cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            // Бланк СР-45: «1.» | текст твердження | Так | Ні — зливаємо номер і текст.
            if (cells.Count >= 2 &&
                NumberOnlyQuestion().IsMatch(cells[0].Trim()) &&
                !string.IsNullOrWhiteSpace(cells[1]) &&
                !NumberOnlyQuestion().IsMatch(cells[1].Trim()))
            {
                var number = cells[0].Trim().TrimEnd('.');
                lines.Add($"{number}. {cells[1].Trim()}");
                continue;
            }

            foreach (var cell in cells.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                lines.Add(cell);
            }
        }
    }

    private static string GetCellText(TableCell cell)
        => string.Join(
                " ",
                cell.Elements<Paragraph>()
                    .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t)))
            .Trim();

    private static void AppendParagraphLine(Paragraph paragraph, List<string> lines)
    {
        var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
        if (!string.IsNullOrWhiteSpace(text))
        {
            lines.Add(text);
        }
    }

    private static string NormalizeLine(string line)
    {
        var normalized = line
            .Replace('\u00A0', ' ')
            .Replace('–', '-')
            .Replace('—', '-')
            .Trim();

        normalized = CheckboxChars().Replace(normalized, "☐ ");
        return MultiSpace().Replace(normalized, " ").Trim();
    }

    private static bool IsMetadataLine(string line)
        => IsPersonalHeaderLine(line)
           || line.Equals("№", StringComparison.OrdinalIgnoreCase)
           || line.Equals("N", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Питання", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Посада", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Підрозділ", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Спеціальність", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Військове", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Реєстраційний", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("№з/п", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Питання і твердження", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Текст опитувальника", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Запитання", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Дата", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Вік", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Інструкція", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Инструкция", StringComparison.OrdinalIgnoreCase)
           || ScaleFooter().IsMatch(line);

    private static bool IsPersonalHeaderLine(string line)
        => line.StartsWith("Прізвище", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("П.І.Б", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("П.I.Б", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("ПІБ", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Ім’я", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Ім'я", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Имя", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("По батькові", StringComparison.OrdinalIgnoreCase);

    private static bool IsYesNoHeader(string line)
        => line.Equals("Так", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Ні", StringComparison.OrdinalIgnoreCase)
           || line.Equals("Yes", StringComparison.OrdinalIgnoreCase)
           || line.Equals("No", StringComparison.OrdinalIgnoreCase);

    private static bool IsEndOfQuestionsSection(string line)
        => UnderscoreLine().IsMatch(line)
           || IsPersonalHeaderLine(line)
           || ScaleFooter().IsMatch(line)
           || line.StartsWith("Методика", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Ключ", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Обробка", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Шкала", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Інтерпретація", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Интерпретация", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Мета", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("РЕЗУЛЬТАТИ ОБСТЕЖЕННЯ", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("ВИСНОВКИ", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Обстеження провів", StringComparison.OrdinalIgnoreCase)
           || (line.Contains("Адаптивність 200", StringComparison.OrdinalIgnoreCase) &&
               (line.Contains("П.І.Б", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Вік", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Дата", StringComparison.OrdinalIgnoreCase)));

    private static bool IsTitleCandidate(string line)
        => line.Contains("ТЕСТ", StringComparison.OrdinalIgnoreCase)
           || line.Contains("Самооцінка", StringComparison.OrdinalIgnoreCase)
           || IsAdaptivity200Title(line)
           || IsZbroyaTitle(line)
           || IsHorskaTitle(line)
           || IsAssingerTitle(line)
           || IsNpnaTitle(line);

    private static string CleanTitle(string line)
    {
        var cleaned = line.Replace('«', ' ').Replace('»', ' ');
        var colonIndex = cleaned.IndexOf(':');
        if (colonIndex > 0 && colonIndex < cleaned.Length - 1)
        {
            // Keep short titles with colon (СР-45...), trim only very long tails after methodology name.
            if (cleaned.Length > 90)
            {
                cleaned = cleaned[..colonIndex].Trim();
            }
        }

        return MultiSpace().Replace(cleaned, " ").Trim();
    }

    [GeneratedRegex(@"^(\d+)\.\s+(.*)$")]
    private static partial Regex NumberedQuestion();

    [GeneratedRegex(@"^([IVXІХivxіх]{1,6})\.\s*(.+)$")]
    private static partial Regex RomanQuestion();

    [GeneratedRegex(@"^([1-3])\.\s*(.+)$")]
    private static partial Regex AssingerOption();

    [GeneratedRegex(@"^(\d+)\.?\s*$")]
    private static partial Regex NumberOnlyQuestion();

    [GeneratedRegex(@"^\s*Д_{2,}.*ПР", RegexOptions.IgnoreCase)]
    private static partial Regex ScaleFooter();

    [GeneratedRegex(@"^☐?\s*([A-Za-zА-Яа-яІіЇїЄє])\.\s*(.+)$")]
    private static partial Regex LetterOption();

    [GeneratedRegex(@"☐\s*(?<opt>Так|Ні|Yes|No)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YesNoOption();

    [GeneratedRegex(@"Оберіть\s+від\s+(\d+)\s+до\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ScaleHint();

    [GeneratedRegex(@"^1(\s+\d+){3,}$")]
    private static partial Regex ScaleNumbers();

    [GeneratedRegex(@"^Відповідь\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex AnswerBlank();

    [GeneratedRegex(@"^(Інструкція|Инструкция)\s*[:.\-–—]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex InstructionPrefix();

    [GeneratedRegex(@"^_{5,}$")]
    private static partial Regex UnderscoreLine();

    [GeneratedRegex(@"[☐☑□■◻◼]")]
    private static partial Regex CheckboxChars();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();
}
