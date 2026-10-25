using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Coclico.Modules.UpdateCenter.Models;
using Coclico.Modules.UpdateCenter.Services;

namespace Coclico.Modules.UpdateCenter.ViewModels;

public enum UpdateCenterState
{
    Idle,
    Scanning,
    Ready,
    Updating
}

public sealed class UpdateCenterViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly UpdateScanService _scanService;
    private readonly WingetUpdateService _wingetService;
    private readonly RestorePointService _restorePointService;
    private CancellationTokenSource? _cts;
    private UpdateCenterState _state = UpdateCenterState.Idle;
    private DateTimeOffset? _lastScan;
    private string? _lastScanSummary;

    public UpdateCenterViewModel(
        UpdateScanService scanService,
        WingetUpdateService wingetService,
        RestorePointService restorePointService)
    {
        _scanService = scanService;
        _wingetService = wingetService;
        _restorePointService = restorePointService;

        CheckAllCommand = new RelayCommand(_ => CheckAll(), _ => State is UpdateCenterState.Idle or UpdateCenterState.Ready);
        UpdateSelectedCommand = new RelayCommand(_ => _ = UpdateSelectedAsync(), _ => State == UpdateCenterState.Ready && SelectedItems.Any());

        Sections.Add(Applications);
        Sections.Add(Drivers);
        Sections.Add(SystemSection);
    }

    public ObservableCollection<UpdateSectionViewModel> Sections { get; } = [];

    public UpdateSectionViewModel Applications { get; } = new() { Kind = UpdateKind.Application, IsExpanded = true };
    public UpdateSectionViewModel Drivers { get; } = new() { Kind = UpdateKind.Driver };
    public UpdateSectionViewModel SystemSection { get; } = new() { Kind = UpdateKind.Windows };

    public ICommand CheckAllCommand { get; }
    public ICommand UpdateSelectedCommand { get; }

    public UpdateCenterState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public DateTimeOffset? LastScan
    {
        get => _lastScan;
        private set
        {
            if (_lastScan == value) return;
            _lastScan = value;
            OnPropertyChanged();
        }
    }

    public string? LastScanSummary
    {
        get => _lastScanSummary;
        private set
        {
            if (_lastScanSummary == value) return;
            _lastScanSummary = value;
            OnPropertyChanged();
        }
    }

    public IEnumerable<UpdateItemViewModel> SelectedItems => Sections.SelectMany(s => s.Items).Where(i => i.IsSelected);

    public void CheckAll()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _ = CheckAllAsync(_cts.Token);
    }

    private async Task CheckAllAsync(CancellationToken cancellationToken)
    {
        State = UpdateCenterState.Scanning;
        foreach (var section in Sections)
        {
            section.Items.Clear();
            section.IsBusy = true;
        }

        try
        {
            var result = await Task.Run(() => _scanService.ScanAllAsync(cancellationToken), cancellationToken).ConfigureAwait(true);

            foreach (var item in result.Items)
                SectionFor(item.Kind).Items.Add(new UpdateItemViewModel { Model = item, IsSelected = false });

            LastScan = result.ScannedAt;
            LastScanSummary = result.HasFailures
                ? $"{result.AvailableCount} mise(s) à jour · {result.SourceErrors.Count} source(s) en erreur"
                : $"{result.AvailableCount} mise(s) à jour";

            State = UpdateCenterState.Ready;
        }
        catch (OperationCanceledException)
        {
            State = UpdateCenterState.Idle;
        }
        catch
        {
            LastScanSummary = "Analyse interrompue";
            State = UpdateCenterState.Idle;
        }
        finally
        {
            foreach (var section in Sections)
            {
                section.IsBusy = false;
                section.RefreshCount();
            }
        }
    }

    public async Task UpdateSelectedAsync()
    {
        var selected = SelectedItems.Where(i => i.Kind == UpdateKind.Application).ToList();
        if (selected.Count == 0 || State == UpdateCenterState.Updating)
            return;

        State = UpdateCenterState.Updating;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var hasDriver = SelectedItems.Any(i => i.Kind == UpdateKind.Driver);
        if (hasDriver)
            _ = _restorePointService.Create("Coclico — mise à jour de pilotes");

        var success = 0;
        var failures = new List<string>();

        foreach (var item in selected)
        {
            item.SetStatus(UpdateItemStatus.Updating);
            try
            {
                var upgraded = await _wingetService.UpgradeAsync(item.Id, _cts.Token).ConfigureAwait(true);
                item.SetStatus(upgraded ? UpdateItemStatus.Succeeded : UpdateItemStatus.Failed);
                if (upgraded) success++;
                else failures.Add(item.Name);
            }
            catch (OperationCanceledException)
            {
                item.SetStatus(UpdateItemStatus.Skipped);
                break;
            }
            catch (Exception ex)
            {
                item.SetStatus(UpdateItemStatus.Failed);
                failures.Add($"{item.Name} ({ex.Message})");
            }
        }

        LastScanSummary = failures.Count == 0
            ? $"{success} application(s) mise(s) à jour"
            : $"{success} réussie(s) · {failures.Count} échec(s)";

        await CheckAllAsync(_cts.Token).ConfigureAwait(true);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        State = UpdateCenterState.Idle;
    }

    private UpdateSectionViewModel SectionFor(UpdateKind kind) => kind switch
    {
        UpdateKind.Application => Applications,
        UpdateKind.Driver => Drivers,
        _ => SystemSection
    };

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class RelayCommand(Action<object?> execute, Func<object?, bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => canExecute(parameter);
        public void Execute(object? parameter) => execute(parameter);
    }
}
