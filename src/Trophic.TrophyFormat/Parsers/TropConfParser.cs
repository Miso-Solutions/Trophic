using System.Xml.Linq;
using Trophic.TrophyFormat.Enums;
using Trophic.TrophyFormat.Exceptions;
using Trophic.TrophyFormat.Models;

namespace Trophic.TrophyFormat.Parsers;

/// <summary>
/// Parses TROPCONF.SFM which is XML with an optional 0x40-byte binary header on real PS3.
/// RPCS3 format has no header — XML starts at offset 0.
/// </summary>
public sealed class TropConfParser
{
    private const int Ps3HeaderSize = 0x40;
    private const string ConfFileName = "TROPCONF.SFM";

    public string TitleName { get; private set; } = string.Empty;
    public bool HasPlatinum { get; private set; }

    private readonly List<TrophyDefinition> _trophies = new();
    private readonly Dictionary<int, string> _groupNames = new();
    public int Count => _trophies.Count;
    public TrophyDefinition this[int index] => _trophies[index];

    /// <summary>
    /// Returns the group name for a given group ID, or null if not found.
    /// Group 0 = base game (no entry in this dictionary).
    /// </summary>
    public string? GetGroupName(int groupId) =>
        _groupNames.TryGetValue(groupId, out var name) ? name : null;

    public TropConfParser(string directoryPath, bool isRpcs3Format)
    {
        var filePath = Path.Combine(directoryPath, ConfFileName);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Trophy configuration file not found: {filePath}");

        Parse(filePath, isRpcs3Format);
    }

    private void Parse(string filePath, bool isRpcs3Format)
    {
        var fileData = File.ReadAllBytes(filePath);

        // Determine XML start offset
        int xmlOffset = 0;
        if (!isRpcs3Format && fileData.Length > Ps3HeaderSize)
        {
            // Check if data at offset 0 looks like XML
            if (fileData[0] != '<')
                xmlOffset = Ps3HeaderSize;
        }

        // Find the XML content (skip any null bytes at end)
        int xmlEnd = fileData.Length;
        while (xmlEnd > xmlOffset && fileData[xmlEnd - 1] == 0)
            xmlEnd--;

        var xmlContent = System.Text.Encoding.UTF8.GetString(fileData, xmlOffset, xmlEnd - xmlOffset);
        var doc = XDocument.Parse(xmlContent);
        var root = doc.Root;

        if (root == null || root.Name.LocalName != "trophyconf")
            throw new InvalidTrophyFileException("Invalid TROPCONF.SFM: root element is not 'trophyconf'");

        TitleName = root.Element("title-name")?.Value ?? string.Empty;

        // Parse group names (DLC packs): <group id="001"><name>Far East Tour</name></group>
        foreach (var groupElem in root.Elements("group"))
        {
            if (int.TryParse(groupElem.Attribute("id")?.Value, out var groupId))
            {
                var groupName = groupElem.Element("name")?.Value;
                if (!string.IsNullOrEmpty(groupName))
                    _groupNames[groupId] = groupName;
            }
        }

        foreach (var elem in root.Elements("trophy"))
        {
            var trophy = new TrophyDefinition
            {
                Id = int.Parse(elem.Attribute("id")?.Value ?? "0"),
                Hidden = elem.Attribute("hidden")?.Value == "yes",
                Type = TrophyTypeExtensions.FromCode(elem.Attribute("ttype")?.Value ?? "B"),
                GroupId = int.TryParse(elem.Attribute("gid")?.Value, out var gid) ? gid : 0,
                ParentId = int.TryParse(elem.Attribute("pid")?.Value, out var pid) ? pid : 0,
                Name = elem.Element("name")?.Value ?? string.Empty,
                Detail = elem.Element("detail")?.Value ?? string.Empty
            };

            _trophies.Add(trophy);
        }

        // Check if first trophy is platinum
        HasPlatinum = _trophies.Count > 0 && _trophies[0].Type == TrophyType.Platinum;
    }
}
