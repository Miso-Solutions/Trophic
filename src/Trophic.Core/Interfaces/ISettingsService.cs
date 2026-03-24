namespace Trophic.Core.Interfaces;

public interface ISettingsService
{
    string LanguageCode { get; set; }
    string? LastBrowseDirectory { get; set; }
    void Save();
    void Load();
}
