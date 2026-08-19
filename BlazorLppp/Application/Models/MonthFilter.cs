using System.Globalization;

namespace BlazorLppp.Application.Models;

public static class MonthFilter
{
    public static string CurrentValue => DateTime.Now.ToString("yyyy-MM");

    public static DateOnly? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[4] != '-')
        {
            return null;
        }

        if (!int.TryParse(value[..4], out var year) ||
            !int.TryParse(value[5..], out var month) ||
            month is < 1 or > 12)
        {
            return null;
        }

        return new DateOnly(year, month, 1);
    }

    public static IReadOnlyList<(string Value, string Label)> BuildOptions(int monthsBack = 24)
    {
        var uk = CultureInfo.GetCultureInfo("uk-UA");
        var start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var count = Math.Max(1, monthsBack);
        var items = new List<(string Value, string Label)>(count);
        for (var i = 0; i < count; i++)
        {
            var date = start.AddMonths(-i);
            var raw = date.ToString("MMMM yyyy", uk);
            var label = string.IsNullOrEmpty(raw)
                ? date.ToString("yyyy-MM")
                : char.ToUpper(raw[0], uk) + raw[1..];
            items.Add((date.ToString("yyyy-MM"), label));
        }

        return items;
    }
}
