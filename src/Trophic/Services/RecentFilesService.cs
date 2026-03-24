using System.IO;
using System.Text.Json;

namespace Trophic.Services;

public sealed class RecentFileEntry
{
    public string Path { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }

    public string RelativeTime
    {
        get
        {
            var elapsed = DateTime.UtcNow - LastOpened;
            if (elapsed.TotalMinutes < 1) return "just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
            if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays}d ago";
            return LastOpened.ToString("MMM d, yyyy");
        }
    }
}

public sealed class RecentFilesService
{
    private const int MaxEntries = 5;
    private static readonly string StorePath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "recent.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public List<RecentFileEntry> GetEntries()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<RecentFileEntry>>(json, JsonOptions) ?? [];
        }
        catch (Exception) // Non-critical — corrupt or unreadable recent file
        {
            return [];
        }
    }

    public void AddEntry(string path, string gameName)
    {
        var entries = GetEntries();
        entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, new RecentFileEntry
        {
            Path = path,
            GameName = gameName,
            LastOpened = DateTime.UtcNow
        });

        if (entries.Count > MaxEntries)
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

        Save(entries);
    }

    public void RemoveEntry(string path)
    {
        var entries = GetEntries();
        entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        Save(entries);
    }

    private static void Save(List<RecentFileEntry> entries)
    {
        try
        {
            File.WriteAllText(StorePath, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch (Exception) // Non-critical — silently ignore write failures
        {
        }
    }
}
