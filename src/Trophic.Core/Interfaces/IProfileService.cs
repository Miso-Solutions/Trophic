namespace Trophic.Core.Interfaces;

public interface IProfileService
{
    IReadOnlyList<string> GetProfileNames();
    void SaveProfile(string name, string sourceSfoPath);
    void DeleteProfile(string name);
    string? GetProfilePath(string name);
}
