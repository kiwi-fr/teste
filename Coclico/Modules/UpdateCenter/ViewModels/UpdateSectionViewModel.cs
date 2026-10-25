using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.ViewModels;

public sealed class UpdateSectionViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isBusy;

    public required UpdateKind Kind { get; init; }

    public string Title => Kind switch
    {
        UpdateKind.Application => "Applications",
        UpdateKind.Driver => "Pilotes",
        UpdateKind.Windows => "Système",
        UpdateKind.Coclico => "Coclico",
        _ => "Autres"
    };

    public ObservableCollection<UpdateItemViewModel> Items { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public int AvailableCount => Items.Count(i => i.IsSelectable);

    public void RefreshCount() => OnPropertyChanged(nameof(AvailableCount));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
