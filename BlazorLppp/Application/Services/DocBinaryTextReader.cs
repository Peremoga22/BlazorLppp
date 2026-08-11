using System.Text;
using System.Text.RegularExpressions;

namespace BlazorLppp.Application.Services;

/// <summary>
/// Читає текст із застарілого .doc (OLE) без Microsoft Word — для парсингу опитувальників.
/// </summary>
public static partial class DocBinaryTextReader
{
    public static IReadOnlyList<string> ReadLines(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var unicode = Encoding.Unicode.GetString(bytes);
        var lines = ExtractReadableLines(unicode);

        if (lines.Count < 10)
        {
            var ansi = Encoding.GetEncoding(1251).GetString(bytes);
            lines = ExtractReadableLines(ansi);
        }

        return lines;
    }

    private static List<string> ExtractReadableLines(string text)
    {
        var chunks = ReadableChunk().Matches(text)
            .Select(m => Normalize(m.Value))
            .Where(v => v.Length >= 1 && HasUsefulContent(v))
            .ToList();

        // Розбиваємо довгі зліплені фрагменти по типових межах питань.
        var lines = new List<string>();
        foreach (var chunk in chunks)
        {
            if (NumberedSplit().IsMatch(chunk) && chunk.Length > 40)
            {
                var parts = NumberedSplit().Split(chunk)
                    .Select(Normalize)
                    .Where(v => v.Length > 0);
                lines.AddRange(parts);
                continue;
            }

            lines.Add(chunk);
        }

        return DeduplicateConsecutive(lines);
    }

    private static List<string> DeduplicateConsecutive(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        string? previous = null;
        foreach (var line in lines)
        {
            if (string.Equals(previous, line, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(line);
            previous = line;
        }

        return result;
    }

    private static string Normalize(string value)
    {
        var normalized = value
            .Replace('\u00A0', ' ')
            .Replace('–', '-')
            .Replace('—', '-')
            .Trim();
        return MultiSpace().Replace(normalized, " ");
    }

    private static bool HasUsefulContent(string value)
        => value.Any(ch => char.IsLetter(ch) || char.IsDigit(ch));

    [GeneratedRegex(@"[\u0400-\u04FFA-Za-z0-9№«»ʼ’'""\-\.,;:!\?\(\)/%\s]{1,500}")]
    private static partial Regex ReadableChunk();

    [GeneratedRegex(@"(?=\d+\.\s+\S)")]
    private static partial Regex NumberedSplit();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();
}
