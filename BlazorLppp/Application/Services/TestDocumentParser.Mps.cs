using System.Text.RegularExpressions;

using BlazorLppp.Application.Models;
using BlazorLppp.Domain.Enums;

namespace BlazorLppp.Application.Services;

/// <summary>
/// Розбір «Опитувальника щодо визначення морально-психологічного стану» (МПС, Додаток 2 до Інструкції).
/// Бланк — таблиця: «№ з/п» → текст критерію → рядок шкали «0 1 2 … 10».
/// </summary>
public partial class TestDocumentParser
{
    private static readonly Regex MpsItemNumber = new(@"^\d{1,2}\.\d{1,2}$", RegexOptions.Compiled);
    private static readonly Regex MpsHeading = new(@"^([1-6])\.\s+(\D.*)$", RegexOptions.Compiled);
    private static readonly Regex MpsOptionLine = new(@"^\d\.\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex MpsInlineOptionsStart = new(@"(?:^|\s)1\.\s+\S", RegexOptions.Compiled);
    private static readonly Regex MpsOptionSplit = new(@"\s(?=\d\.\s)", RegexOptions.Compiled);
    private static readonly Regex MpsScaleRow = new(@"^0(\s+\d{1,2}){10}$", RegexOptions.Compiled);
    private static readonly Regex MpsFileNameWord = new(
        @"(?<![\p{L}])МПС(?![\p{L}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool IsMpsFileName(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        return MpsFileNameWord.IsMatch(name) ||
               name.Contains("морально-психолог", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Суворий збіг за структурою бланка — щоб не сплутати з іншими тестами про МПС.</summary>
    internal static bool LooksLikeMpsLines(IReadOnlyList<string> lines)
    {
        var text = string.Join('\n', lines);
        return text.Contains("Моральна (духовна, національно-патріотична) налаштованість", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("Емоційно-вольова налаштованість", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("Фахова здатність", StringComparison.OrdinalIgnoreCase) &&
               lines.Count(line => MpsScaleRow.IsMatch(line.Trim())) >= 20;
    }

    internal static bool IsCompleteMpsDocument(ParsedTestDocument parsed)
        => parsed.Questions.Count == MpsDocumentTemplate.QuestionCount &&
           parsed.Questions.All(q => !string.IsNullOrWhiteSpace(q.Text) && q.Text.Any(char.IsLetter)) &&
           parsed.Questions.Count(q => q.Type == QuestionType.Scale) == MpsDocumentTemplate.QuestionCount - 1 &&
           parsed.Questions.Any(q =>
               q.SortOrder == MpsDocumentTemplate.MotivationQuestionOrder &&
               q.Type == QuestionType.MultiChoice &&
               q.Options.Count >= 2);

    /// <summary>Повертає розібраний бланк або <c>null</c>, якщо структура неповна (тоді береться шаблон).</summary>
    internal static ParsedTestDocument? ParseMps(IReadOnlyList<string> lines)
    {
        var result = ParseMpsCore(lines);
        return IsCompleteMpsDocument(result) ? result : null;
    }

    internal static ParsedTestDocument ParseMpsCore(IReadOnlyList<string> lines)
    {
        var questions = new List<ParsedTestQuestion>();
        var sectionHint = string.Empty;

        ParsedTestQuestion? current = null;
        var textParts = new List<string>();
        var inItem = false;
        var multi = false;

        void Finish()
        {
            if (current is not null && multi)
            {
                current.Text = current.Text.Replace(" (підкресли)", string.Empty, StringComparison.OrdinalIgnoreCase);
                current.Type = QuestionType.MultiChoice;
                current.ScaleMin = null;
                current.ScaleMax = null;
                current.MaxSelections = ResolveMpsMaxSelections(current.Text);
                questions.Add(current);
            }

            current = null;
            textParts.Clear();
            inItem = false;
            multi = false;
        }

        foreach (var raw in lines)
        {
            var line = Clean(raw);
            if (line.Length == 0)
            {
                continue;
            }

            if (MpsItemNumber.IsMatch(line))
            {
                Finish();
                inItem = true;
                current = new ParsedTestQuestion
                {
                    SortOrder = questions.Count + 1,
                    Hint = string.IsNullOrEmpty(sectionHint) ? null : sectionHint,
                    Type = QuestionType.Scale,
                    ScaleMin = MpsDocumentTemplate.ScaleMinValue,
                    ScaleMax = MpsDocumentTemplate.ScaleMaxValue
                };
                continue;
            }

            if (!inItem)
            {
                var heading = MpsHeading.Match(line);
                if (heading.Success)
                {
                    sectionHint = line;
                }

                continue;
            }

            if (MpsScaleRow.IsMatch(line))
            {
                current!.Text = string.Join(' ', textParts).Trim();
                questions.Add(current);
                current = null;
                textParts.Clear();
                inItem = false;
                continue;
            }

            // Варіанти відповіді можуть бути як окремими рядками, так і в одному рядку з питанням.
            var inline = MpsInlineOptionsStart.Match(line);
            if (!multi && inline.Success)
            {
                var head = line[..inline.Index].Trim();
                if (head.Length > 0)
                {
                    textParts.Add(head);
                }

                if (textParts.Count > 0)
                {
                    multi = true;
                    current!.Text = string.Join(' ', textParts).Trim();
                    foreach (var part in MpsOptionSplit.Split(line[inline.Index..].Trim()))
                    {
                        AddMpsOption(current, part);
                    }

                    continue;
                }
            }

            var option = MpsOptionLine.Match(line);
            if (option.Success && (multi || textParts.Count > 0))
            {
                multi = true;
                current!.Text = string.Join(' ', textParts).Trim();
                AddMpsOption(current, line);
                continue;
            }

            if (!multi)
            {
                textParts.Add(line);
                continue;
            }

            // Продовження довгого варіанта відповіді в наступному рядку.
            if (current!.Options.Count > 0)
            {
                var last = current.Options[^1];
                if (!last.Text.Equals("Інше", StringComparison.Ordinal))
                {
                    last.Text = $"{last.Text} {line}".Trim();
                }
            }
        }

        Finish();

        for (var i = 0; i < questions.Count; i++)
        {
            questions[i].SortOrder = i + 1;
        }

        var result = new ParsedTestDocument
        {
            Title = MpsDocumentTemplate.CanonicalTitle,
            Instruction = MpsDocumentTemplate.CanonicalInstruction,
            Questions = questions
        };

        return result;

        static string Clean(string value)
            => Regex.Replace(value.Replace('’', '\'').Replace('–', '-').Replace(' ', ' '), @"\s+", " ").Trim();
    }

    private static void AddMpsOption(ParsedTestQuestion question, string rawOption)
    {
        var match = MpsOptionLine.Match(rawOption.Trim());
        var text = match.Success ? match.Groups[1].Value.Trim() : rawOption.Trim();
        if (text.StartsWith("Інше", StringComparison.OrdinalIgnoreCase))
        {
            text = "Інше";
        }

        if (text.Length == 0)
        {
            return;
        }

        question.Options.Add(new ParsedTestOption
        {
            SortOrder = question.Options.Count + 1,
            Key = (question.Options.Count + 1).ToString(),
            Text = text
        });
    }

    private static int? ResolveMpsMaxSelections(string text)
    {
        if (text.Contains("два", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (text.Contains("три", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 2;
    }
}
