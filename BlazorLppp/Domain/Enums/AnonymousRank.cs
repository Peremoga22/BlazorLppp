namespace BlazorLppp.Domain.Enums;

public enum AnonymousRank
{
    Soldier = 1,
    Sergeant = 2,
    Officer = 3
}

public static class AnonymousRankNames
{
    public static string Display(AnonymousRank rank) => rank switch
    {
        AnonymousRank.Soldier => "Солдат",
        AnonymousRank.Sergeant => "Сержант",
        AnonymousRank.Officer => "Офіцер",
        _ => "—"
    };

    public static string Folder(AnonymousRank rank) => rank switch
    {
        AnonymousRank.Soldier => "Солдати",
        AnonymousRank.Sergeant => "Сержанти",
        AnonymousRank.Officer => "Офіцери",
        _ => "Інше"
    };

    public static string Plural(AnonymousRank rank) => Folder(rank);
}
