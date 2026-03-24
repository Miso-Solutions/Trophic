using Trophic.TrophyFormat.Enums;

namespace Trophic.TrophyFormat.Models;

/// <summary>
/// A single trophy definition from TROPCONF.SFM (XML).
/// </summary>
public sealed class TrophyDefinition
{
    public int Id { get; init; }
    public bool Hidden { get; init; }
    public TrophyType Type { get; init; }
    public int GroupId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
