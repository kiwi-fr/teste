using System.ComponentModel;
using System.Runtime.CompilerServices;
using Coclico.Services;

namespace Coclico.ViewModels;

/// <summary>
/// Entrée de démarrage prête pour l'affichage dans la vue Santé &amp; Défense.
/// Le basculement passe par le gestionnaire du ViewModel : si le service refuse
/// (entrée HKLM protégée), la propriété re-notifie l'UI pour revenir à l'état réel.
/// </summary>
public sealed class StartupEntryItem : INotifyPropertyChanged
{
    private readonly Func<StartupEntryItem, bool, bool> _toggle;
    private bool _isEnabled;

    public StartupEntryItem(StartupEntry entry, Func<StartupEntryItem, bool, bool> toggle)
    {
        Entry = entry;
        _toggle = toggle;
        _isEnabled = entry.IsEnabled;
    }

    public StartupEntry Entry { get; }

    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Source => Entry.Source;
    public bool CanToggle => Entry.CanToggle;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            if (_toggle(this, value))
            {
                _isEnabled = value;
            }

            // Notifie toujours : si le changement a été refusé, l'interrupteur
            // de l'UI revient visuellement à l'état réel de l'entrée.
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
