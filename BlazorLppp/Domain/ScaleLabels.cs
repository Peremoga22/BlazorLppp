namespace BlazorLppp.Domain;

public static class ScaleLabels
{
    public static string ForValue(int value) => $"{value}.";

    public static string ForDisplay(string? text, string? key = null)
    {
        var raw = string.IsNullOrWhiteSpace(text) ? key : text;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        var core = trimmed.EndsWith('.') ? trimmed[..^1].TrimEnd() : trimmed;
        return int.TryParse(core, out var number) ? ForValue(number) : trimmed;
    }
}
