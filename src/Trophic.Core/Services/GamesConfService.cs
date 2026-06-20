namespace Trophic.Core.Services;

/// <summary>
/// Loads Apollo Save Tool's games.conf file from disk and exposes per-game
/// disc_hash_key and per-file secure_file_id lookups.
/// File format (one section per game family):
///   [BCES12345,BLES12345]
///   disc_hash_key=AABBCC...32hex
///   secure_file_id:SAVE.BIN=AABBCC...32hex
///   secure_file_id:DATA.BIN=AABBCC...32hex
/// Trophic does not ship games.conf. The user provides it (typically by copying
/// from an Apollo install) by placing the file in the application directory.
/// </summary>
public sealed class GamesConfService
{
    private const string FileName = "games.conf";

    private readonly Dictionary<string, GameKeys> _gamesByTitleId = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoaded => _gamesByTitleId.Count > 0;
    public int GameCount => _gamesByTitleId.Values.Distinct().Count();

    public GamesConfService()
    {
        Reload();
    }

    public void Reload()
    {
        _gamesByTitleId.Clear();
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
        if (!File.Exists(path)) return;

        GameKeys? current = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            // Strip whole-line and inline comments (";" or "#" preceded by whitespace).
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#') continue;
            var line = StripInlineComment(trimmed).TrimEnd();

            if (line.StartsWith('['))
            {
                int closeIdx = line.IndexOf(']');
                if (closeIdx <= 1) continue;
                var section = line[1..closeIdx];
                current = new GameKeys();
                foreach (var id in section.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    _gamesByTitleId[id] = current;
                continue;
            }

            if (current == null) continue;

            // Apollo's INI parser tries "=" first and only falls back to ":" when no "="
            // is present. This matters because the "secure_file_id:FILE.BIN=hex" entries
            // contain a colon inside the name portion.
            int sep = line.IndexOf('=');
            if (sep < 0) sep = line.IndexOf(':');
            if (sep < 0) continue;
            var name = line[..sep].Trim();
            var value = line[(sep + 1)..].Trim();

            if (name.Equals("disc_hash_key", StringComparison.OrdinalIgnoreCase) && value.Length == 32)
            {
                try { current.DiscHashKey = Convert.FromHexString(value); } catch { /* skip */ }
            }
            else if (name.StartsWith("secure_file_id:", StringComparison.OrdinalIgnoreCase) && value.Length == 32)
            {
                var fileName = name["secure_file_id:".Length..].Trim();
                try { current.SecureFileIds[fileName] = Convert.FromHexString(value); } catch { /* skip */ }
            }
        }
    }

    /// <summary>
    /// Removes a trailing ';' or '#' comment from a line, preserving the line otherwise.
    /// Only strips when the comment marker is preceded by whitespace, matching Apollo's
    /// behaviour, so that ';' inside a value (e.g. file paths) is not mis-parsed.
    /// </summary>
    private static string StripInlineComment(string line)
    {
        bool seenSpace = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (seenSpace && (c == ';' || c == '#'))
                return line[..i];
            seenSpace = char.IsWhiteSpace(c);
        }
        return line;
    }

    public byte[]? GetDiscHashKey(string titleId)
        => _gamesByTitleId.TryGetValue(titleId, out var k) ? k.DiscHashKey : null;

    public byte[]? GetSecureFileId(string titleId, string fileName)
    {
        if (!_gamesByTitleId.TryGetValue(titleId, out var k)) return null;

        // Exact match first
        if (k.SecureFileIds.TryGetValue(fileName, out var id)) return id;

        // Apollo-style wildcard match (?, *) — return first matching pattern
        foreach (var kv in k.SecureFileIds)
            if (WildcardMatch(fileName, kv.Key))
                return kv.Value;
        return null;
    }

    /// <summary>
    /// Apollo-compatible filename wildcard match. '?' = any single char, '*' = any sequence.
    /// Case-insensitive.
    /// </summary>
    private static bool WildcardMatch(string name, string pattern)
    {
        int ni = 0, pi = 0, starPi = -1, starNi = -1;
        while (ni < name.Length)
        {
            if (pi < pattern.Length && (pattern[pi] == '?' || char.ToUpperInvariant(pattern[pi]) == char.ToUpperInvariant(name[ni])))
            {
                ni++; pi++;
            }
            else if (pi < pattern.Length && pattern[pi] == '*')
            {
                starPi = pi++;
                starNi = ni;
            }
            else if (starPi >= 0)
            {
                pi = starPi + 1;
                ni = ++starNi;
            }
            else return false;
        }
        while (pi < pattern.Length && pattern[pi] == '*') pi++;
        return pi == pattern.Length;
    }

    private sealed class GameKeys
    {
        public byte[]? DiscHashKey { get; set; }
        public Dictionary<string, byte[]> SecureFileIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
