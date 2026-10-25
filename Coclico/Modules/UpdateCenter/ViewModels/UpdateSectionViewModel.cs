using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.ViewModels;

public sealed class UpdateSectionViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isBusy;
    private string? _activeFilter;

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

    /// <summary>
    /// Filtre les éléments affichés par nom (insensible à la casse). Chaîne vide ou nulle = pas de filtre.
    /// Important : ne crée pas la vue par défaut tant qu'aucun filtre n'a été activé. Un CollectionView
    /// attache la collection à son thread de création et interdit ensuite toute modification depuis
    /// un autre thread (NotSupportedException hors thread Dispatcher).
    /// </summary>
    public void ApplyFilter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (_activeFilter is null)
                return; // Jamais filtré : rien à faire, et surtout ne pas créer la vue.

            _activeFilter = null;
            CollectionViewSource.GetDefaultView(Items).Filter = null;
            return;
        }

        _activeFilter = text;
        var view = CollectionViewSource.GetDefaultView(Items);
        view.Filter = o => o is UpdateItemViewModel item
            && item.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
