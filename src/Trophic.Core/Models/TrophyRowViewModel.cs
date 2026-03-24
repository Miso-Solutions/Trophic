using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Trophic.Core.Models;

public enum TrophyRowStatus { NotEarned, Earned, Synced }

/// <summary>
/// Flattened view model for a single trophy row in the DataGrid.
/// </summary>
public sealed class TrophyRowViewModel : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string TypeCode { get; init; } = "B";
    public bool IsHidden { get; init; }
    public int GroupId { get; init; }
    public string GroupLabel { get; init; } = "Base Game";
    public bool IsFirstInDlcGroup { get; init; }
    public string? IconPath { get; init; }

    private bool _isEarned;
    public bool IsEarned
    {
        get => _isEarned;
        set { if (_isEarned != value) { _isEarned = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusDisplayName)); } }
    }

    private bool _isSynced;
    public bool IsSynced
    {
        get => _isSynced;
        set { if (_isSynced != value) { _isSynced = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusDisplayName)); } }
    }

    private DateTime? _earnedTime;
    public DateTime? EarnedTime
    {
        get => _earnedTime;
        set { if (_earnedTime != value) { _earnedTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedEarnedDate)); } }
    }

    public string TypeDisplayName => TypeCode switch
    {
        "P" => "Platinum",
        "G" => "Gold",
        "S" => "Silver",
        "B" => "Bronze",
        _ => "Unknown"
    };

    public string FormattedEarnedDate => EarnedTime?.ToString("dddd, MMMM d, yyyy 'at' h:mm tt") ?? "Not earned";

    public string StatusDisplayName => IsSynced ? "Synced with PSN"
        : IsEarned ? "Earned"
        : "Not earned";

    public TrophyRowStatus Status => IsSynced ? TrophyRowStatus.Synced
        : IsEarned ? TrophyRowStatus.Earned
        : TrophyRowStatus.NotEarned;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
