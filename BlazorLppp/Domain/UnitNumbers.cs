namespace BlazorLppp.Domain;

/// <summary>
/// Базові номери підрозділів (сідер створює 1–5; далі можна додавати нові).
/// </summary>
public static class UnitNumbers
{
    public const int Min = 1;

    public const int Max = 5;

    public static readonly int[] All = [1, 2, 3, 4, 5];

    public static bool IsValid(int numberUnit) => numberUnit >= Min;
}
