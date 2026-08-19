using System.Globalization;

namespace BlazorLppp.Application.Models;

/// <summary>
/// Фільтр за календарним місяцем без року: січень…грудень (усі роки).
/// </summary>
public static class MonthFilter
{
    public static string CurrentValue => DateTime.Now.Month.ToString("00");

    public static int? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (int.TryParse(trimmed, out var month) && month is >= 1 and <= 12)
        {
            return month;
        }

        // Старий формат yyyy-MM — беремо лише місяць.
        if (trimmed.Length == 7 &&
            trimmed[4] == '-' &&
            int.TryParse(trimmed[5..], out var legacyMonth) &&
            legacyMonth is >= 1 and <= 12)
        {
            return legacyMonth;
        }

        return null;
    }

    public static IReadOnlyList<(string Value, string Label)> BuildOptions()
    {
        var uk = CultureInfo.GetCultureInfo("uk-UA");
        var items = new List<(string Value, string Label)>(12);
        for (var month = 1; month <= 12; month++)
        {
            var raw = uk.DateTimeFormat.GetMonthName(month);
            var label = string.IsNullOrEmpty(raw)
                ? month.ToString("00")
                : char.ToUpper(raw[0], uk) + raw[1..];
            items.Add((month.ToString("00"), label));
        }

        return items;
    }
}
