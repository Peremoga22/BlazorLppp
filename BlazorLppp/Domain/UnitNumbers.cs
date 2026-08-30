namespace BlazorLppp.Domain;

/// <summary>
/// Базові номери підрозділів (сідер створює 1–5; далі можна додавати нові).
/// </summary>
public static class UnitNumbers
{
    public const int Min = 1;

    public const int Max = 5;

    public const int Management = 4;

    public const int Logistics = 5;

    public static readonly int[] All = [1, 2, 3, 4, 5];

    public static bool IsValid(int numberUnit) => numberUnit >= Min;

    /// <summary>
    /// Типова назва: підрозділи 1–3 залишаються «Підрозділ N»,
    /// 4 — «управління», 5 — «логістика».
    /// </summary>
    public static string GetDefaultName(int numberUnit) => numberUnit switch
    {
        Management => "управління",
        Logistics => "логістика",
        _ when numberUnit >= Min => $"Підрозділ {numberUnit}",
        _ => string.Empty
    };

    /// <summary>Короткий підпис вкладки адмінки.</summary>
    public static string GetTabLabel(int numberUnit) => numberUnit switch
    {
        Management => "управління",
        Logistics => "логістика",
        _ => "Підрозділ"
    };

    public static bool IsLegacyGeneratedName(string name, int number) =>
        name.Equals($"Підрозділ {number}", StringComparison.OrdinalIgnoreCase);
}
