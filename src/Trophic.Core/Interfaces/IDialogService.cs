using Trophic.Core.Models;

namespace Trophic.Core.Interfaces;

public interface IDialogService
{
    string? BrowseFolder(string? description = null);
    string? BrowseFile(string filter, string? title = null, string? formatHint = null);
    bool Confirm(string message, string title = "Confirm");
    void ShowError(string message, string title = "Error");
    void ShowInfo(string message, string title = "Info");
    string? PromptText(string message, string title = "Input", string defaultValue = "");
    DateTime? ShowDateTimePicker(DateTime? initial = null, DateTime? min = null, DateTime? max = null, DateTime? now = null);
    ImportSettings? ShowImportDialog();
    CatalogEntry? ShowCatalogDialog();

    /// <summary>
    /// Opens the license activation dialog. Returns true if the user holds a valid
    /// entitlement when the dialog closes (whether already-licensed or just activated).
    /// </summary>
    bool ShowLicenseDialog();
}
