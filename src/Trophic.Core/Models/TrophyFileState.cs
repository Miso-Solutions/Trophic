using Trophic.TrophyFormat.Parsers;

namespace Trophic.Core.Models;

/// <summary>
/// Aggregate state of an open trophy folder.
/// </summary>
public sealed class TrophyFileState
{
    public required string OriginalPath { get; init; }
    public required string TempPath { get; init; }
    public required TropConfParser Config { get; init; }
    public required TropTrnsParser Transactions { get; init; }
    public required TropUsrParser UserState { get; init; }
    public required bool IsRpcs3 { get; init; }
}
