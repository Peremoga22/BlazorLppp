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

        if (Path.GetExtension(filePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return ParseLines(ReadTxtLines(filePath));
        }

        if (WordDocConverter.IsDocExtension(filePath))
        {
            var convertedPath = WordDocConverter.ConvertToDocx(filePath);
            try
            {
                return ParseLines(ReadDocxLines(convertedPath));
            }
            finally
            {
                TryDeleteTempFile(convertedPath);
            }
        }

        return ParseLines(ReadDocxLines(filePath));
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

    internal static ParsedTestDocument ParseLines(IReadOnlyList<string> rawLines)
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
        var isHorska = false;

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
        else if (IsHorskaTitle(result.Title) || lines.Any(IsHorskaTitle))
        {
            isHorska = true;
            result.Title = "Методика вивчення схильності до суїцидальної поведінки (М.В. Горська)";
            defaultType = QuestionType.Scale;
            result.Instruction ??=
                "Проти кожного твердження поставте оцінку за таким принципом: якщо твердження вам підходить — ставте оцінку 2, " +
                "якщо не зовсім підходить — ставте оцінку 1, якщо зовсім не підходить — ставте 0.";
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

            if (isHorska && result.Questions.Count >= 40)
            {
                break;
            }

            if (IsHorskaInstructionLine(line))
            {
                result.Instruction = CleanHorskaInstruction(line);
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
                    if (!string.IsNullOrWhiteSpace(instruction))
                    {
                        result.Instruction = instruction;
                    }
                }

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
                if (isHorska)
                {
                    result.Title = "Методика вивчення схильності до суїцидальної поведінки (М.В. Горська)";
                }
                else if (isAdaptivity200)
                {
                    result.Title = "Адаптивність-200 (БОО)";
                }
                else
                {
                    result.Title = CleanTitle(line);
                }

                continue;
            }

            var numberOnly = NumberOnlyQuestion().Match(line);
            if (numberOnly.Success)
            {
                // У бланку відповідей ідуть лише номери підряд без тексту питань.
                if (pendingNumber.HasValue && result.Questions.Count > 0)
                {
                    break;
                }

                pendingNumber = int.Parse(numberOnly.Groups[1].Value);
                current = null;
                continue;
            }

            var questionMatch = NumberedQuestion().Match(line);
            if (questionMatch.Success)
            {
                pendingNumber = null;
                current = CreateQuestion(
                    int.Parse(questionMatch.Groups[1].Value),
                    questionMatch.Groups[2].Value.Trim(),
                    defaultType);
                if (isHorska)
                {
                    ApplyHorskaScale(current);
                }

                result.Questions.Add(current);
                continue;
            }

            if (pendingNumber.HasValue)
            {
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

            current.Text = $"{current.Text} {line}".Trim();
        }

        FinalizeQuestions(result, defaultType, isHorska);

        if (result.Questions.Count == 0)
        {
            throw new InvalidOperationException(
                "У документі не знайдено питань. Очікується нумерація на кшталт «1. Текст питання?».");
        }

        return result;
    }

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
            if (IsHorskaTitle(line))
            {
                result.Title = "Методика вивчення схильності до суїцидальної поведінки (М.В. Горська)";
                return;
            }

            if (IsAdaptivity200Title(line))
            {
                result.Title = "Адаптивність-200 (БОО)";
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

    internal static bool IsHorskaTitle(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("Горськ", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("М.В. Горська", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("вивчення схильності до суїцидальної поведінки", StringComparison.OrdinalIgnoreCase));

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
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }

        return lines;
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
           || line.StartsWith("Дата", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Вік", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Інструкція", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Инструкция", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Бланк опитувальника", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Бланк", StringComparison.OrdinalIgnoreCase)
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
           || (line.Contains("Адаптивність 200", StringComparison.OrdinalIgnoreCase) &&
               (line.Contains("П.І.Б", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Вік", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Дата", StringComparison.OrdinalIgnoreCase)));

    private static bool IsTitleCandidate(string line)
        => line.Contains("ТЕСТ", StringComparison.OrdinalIgnoreCase)
           || line.Contains("Самооцінка", StringComparison.OrdinalIgnoreCase)
           || IsAdaptivity200Title(line)
           || IsHorskaTitle(line);

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

    [GeneratedRegex(@"^(Інструкція|Инструкция)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex InstructionPrefix();

    [GeneratedRegex(@"^_{5,}$")]
    private static partial Regex UnderscoreLine();

    [GeneratedRegex(@"[☐☑□■◻◼]")]
    private static partial Regex CheckboxChars();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();
}
