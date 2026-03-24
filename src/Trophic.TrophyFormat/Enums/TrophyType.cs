namespace Trophic.TrophyFormat.Enums;

public enum TrophyType
{
    Platinum = 1,
    Gold = 2,
    Silver = 3,
    Bronze = 4
}

public static class TrophyTypeExtensions
{
    public static string ToCode(this TrophyType type) => type switch
    {
        TrophyType.Platinum => "P",
        TrophyType.Gold => "G",
        TrophyType.Silver => "S",
        TrophyType.Bronze => "B",
        _ => "?"
    };

    public static TrophyType FromCode(string code) => code?.ToUpperInvariant() switch
    {
        "P" => TrophyType.Platinum,
        "G" => TrophyType.Gold,
        "S" => TrophyType.Silver,
        "B" => TrophyType.Bronze,
        _ => throw new ArgumentException($"Unknown trophy type code: {code}", nameof(code))
    };

    public static int GradePoints(this TrophyType type) => type switch
    {
        TrophyType.Platinum => 180,
        TrophyType.Gold => 90,
        TrophyType.Silver => 30,
        TrophyType.Bronze => 15,
        _ => 0
    };
}
