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
                "Автоматичний розбір підтримується для файлів .docx та .txt.");
        }

        var lines = Path.GetExtension(filePath).Equals(".txt", StringComparison.OrdinalIgnoreCase)
            ? ReadTxtLines(filePath)
            : ReadDocxLines(filePath);

        return ParseLines(lines);
    }

    internal static ParsedTestDocument ParseLines(IReadOnlyList<string> rawLines)
    {
        var lines = rawLines
            .Select(NormalizeLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var result = new ParsedTestDocument();
        ParsedTestQuestion? current = null;

        foreach (var line in lines)
        {
            if (IsMetadataLine(line))
            {
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

            if (IsTitleCandidate(line) && result.Questions.Count == 0 && current is null)
            {
                result.Title = CleanTitle(line);
                continue;
            }

            var questionMatch = NumberedQuestion().Match(line);
            if (questionMatch.Success)
            {
                current = new ParsedTestQuestion
                {
                    SortOrder = int.Parse(questionMatch.Groups[1].Value),
                    Text = questionMatch.Groups[2].Value.Trim()
                };
                result.Questions.Add(current);
                continue;
            }

            if (current is null)
            {
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

            if (string.IsNullOrWhiteSpace(current.Hint))
            {
                current.Hint = line;
            }
            else
            {
                current.Text = $"{current.Text} {line}".Trim();
            }
        }

        FinalizeQuestions(result);

        if (result.Questions.Count == 0)
        {
            throw new InvalidOperationException(
                "У документі не знайдено питань. Очікується нумерація на кшталт «1. Текст питання?».");
        }

        return result;
    }

    private static void FinalizeQuestions(ParsedTestDocument result)
    {
        foreach (var question in result.Questions)
        {
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
        }
    }

    private static IReadOnlyList<string> ReadTxtLines(string filePath)
        => File.ReadAllLines(filePath, Encoding.UTF8);

    private static IReadOnlyList<string> ReadDocxLines(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false);
        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("Документ Word не містить тексту.");

        var lines = new List<string>();
        foreach (var paragraph in body.Elements<Paragraph>())
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
        => line.StartsWith("Прізвище", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Имя", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Дата", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Інструкція", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("Инструкция", StringComparison.OrdinalIgnoreCase);

    private static bool IsTitleCandidate(string line)
        => line.Contains("ТЕСТ", StringComparison.OrdinalIgnoreCase)
           || line.Contains("Самооцінка", StringComparison.OrdinalIgnoreCase);

    private static string CleanTitle(string line)
        => MultiSpace().Replace(line.Replace('«', ' ').Replace('»', ' '), " ").Trim();

    [GeneratedRegex(@"^(\d+)\.\s+(.*)$")]
    private static partial Regex NumberedQuestion();

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

    [GeneratedRegex(@"[☐☑□■◻◼]")]
    private static partial Regex CheckboxChars();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();
}
