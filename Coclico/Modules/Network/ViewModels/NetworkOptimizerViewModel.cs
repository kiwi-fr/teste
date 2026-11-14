using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Coclico.Models.Network;
using Coclico.Services;
using Coclico.Services.Network;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.ViewModels;

internal class NetworkOptimizerViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxLogEntries = 200;

    private readonly INetworkOptimizerService _netService;
    private readonly NetworkMonitorService? _monitorService;
    private readonly IDisposable? _monitorSubscription;
    private readonly PacketFilterService? _packetFilter;
    private bool _shieldOn;
    private bool _shieldStrict;
    private bool _shieldHeuristics;
    private string _shieldStatsText = "--";
    private CancellationTokenSource? _cts;
    private NetworkAdapterInfo? _selectedAdapter;
    private bool _isBusy;
    private double _progressValue;
    private string _statusText = string.Empty;
    private string _livePingText = "--";
    private string _liveDownloadText = "--";
    private NetworkTestReport? _beforeTest;
    private NetworkTestReport? _afterTest;

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];
    public ObservableCollection<NetworkTuningParameter> TuningParameters { get; } = [];
    public ObservableCollection<string> OutputLog { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RunTestCommand { get; }
    public IAsyncRelayCommand AutoTestedCommand { get; }
    public IAsyncRelayCommand FullTuningCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand ToggleShieldCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;


    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public NetworkOptimizerViewModel()
    {
        _netService = ServiceContainer.GetOptional<INetworkOptimizerService>() ?? new NetworkOptimizerService();
        _monitorService = ServiceContainer.GetOptional<NetworkMonitorService>();
        _packetFilter = ServiceContainer.GetOptional<PacketFilterService>();
        if (_packetFilter != null)
        {
            _packetFilter.StatsChanged += OnShieldStats;
            ShieldOn = _packetFilter.IsRunning;
            var ps = ServiceContainer.GetOptional<SettingsService>();
            ShieldStrict = _packetFilter.Strict = ps?.Settings.PacketShieldStrict ?? true;
            ShieldHeuristics = _packetFilter.HeuristicsEnabled = ps?.Settings.PacketShieldHeuristics ?? true;
        }

        if (_monitorService != null)
        {
            _monitorSubscription = _monitorService.StatsStream.Subscribe(stats =>
            {
                SafeInvokeOnDispatcher(() =>
                {
                    LivePingText = stats.PingMs >= 0 ? $"{stats.PingMs:F0} ms" : "--";
                    LiveDownloadText = stats.DownloadKbps >= 0 ? $"{stats.DownloadKbps / 1024.0:F1} Mbps" : "--";
                });
            });
        }

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsNotBusy);
        RunTestCommand = new AsyncRelayCommand(RunTestAsync, () => IsNotBusy);
        AutoTestedCommand = new AsyncRelayCommand(() => RunTuningAsync(NetworkTuningMode.AutoTested), () => IsNotBusy);
        FullTuningCommand = new AsyncRelayCommand(() => RunTuningAsync(NetworkTuningMode.Full), () => IsNotBusy);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => IsNotBusy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
        ToggleShieldCommand = new RelayCommand(ToggleShield);

        _ = RefreshAsync();
    }

    public bool ShieldAdminOk => _packetFilter?.IsAdmin ?? false;

    public bool ShieldOn
    {
        get => _shieldOn;
        private set
        {
            if (_shieldOn != value)
            {
                _shieldOn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShieldButtonText));
            }
        }
    }

    public string ShieldButtonText => ShieldOn ? Loc.Get("Network_ShieldStop") : Loc.Get("Network_ShieldStart");

    public bool ShieldStrict
    {
        get => _shieldStrict;
        set
        {
            if (_shieldStrict != value)
            {
                _shieldStrict = value;
                if (_packetFilter != null) _packetFilter.Strict = value;
                var s = ServiceContainer.GetOptional<SettingsService>();
                if (s != null)
                {
                    s.Settings.PacketShieldStrict = value;
                    s.Save();
                }
                OnPropertyChanged();
            }
        }
    }

    public bool ShieldHeuristics
    {
        get => _shieldHeuristics;
        set
        {
            if (_shieldHeuristics != value)
            {
                _shieldHeuristics = value;
                if (_packetFilter != null) _packetFilter.HeuristicsEnabled = value;
                var s = ServiceContainer.GetOptional<SettingsService>();
                if (s != null)
                {
                    s.Settings.PacketShieldHeuristics = value;
                    s.Save();
                }
                OnPropertyChanged();
            }
        }
    }

    public string ShieldStatsText
    {
        get => _shieldStatsText;
        private set
        {
            if (!string.Equals(_shieldStatsText, value, StringComparison.Ordinal))
            {
                _shieldStatsText = value;
                OnPropertyChanged();
            }
        }
    }

    private void ToggleShield()
    {
        if (_packetFilter == null) return;
        try
        {
            if (_packetFilter.IsRunning)
            {
                _packetFilter.Stop();
                ShieldOn = false;
                AddLog(Loc.Get("Network_ShieldStoppedLog"));
            }
            else
            {
                if (!_packetFilter.IsAdmin)
                {
                    StatusText = Loc.Get("Network_ShieldAdminRequired");
                    AddLog(Loc.Get("Network_ShieldAdminRequired"));
                    return;
                }

                _packetFilter.Start();
                ShieldOn = true;
                AddLog(Loc.Get("Network_ShieldStartedLog"));
            }


            var s = ServiceContainer.GetOptional<SettingsService>();
            if (s != null)
            {
                s.Settings.PacketShieldEnabled = _packetFilter.IsRunning;
                s.Save();
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.ToggleShield");
            StatusText = ex.Message;
        }
    }

    private void OnShieldStats(PacketFilterStats stats)
    {
        SafeInvokeOnDispatcher(() =>
        {
            ShieldStatsText = $"Paquets vus : {stats.PacketsSeen:N0} — analysés : {stats.PacketsAnalyzed:N0} — inspectés : {stats.Inspected:N0} — bloqués (listes) : {stats.TrackersBlocked:N0} — bloqués (heuristiques) : {stats.HeuristicBlocked:N0}" +
                (stats.LastBlockReason != null ? $" — dernier blocage : {stats.LastBlockReason}" : string.Empty);
        });
    }
    public NetworkAdapterInfo? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            if (!ReferenceEquals(_selectedAdapter, value))
            {
                _selectedAdapter = value;
                OnPropertyChanged();
                if (!IsBusy)
                {
                    _ = LoadTuningStatusAsync();
                }
            }
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                SafeInvokeOnDispatcher(NotifyCommandsCanExecuteChanged);
            }
        }
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RunTestCommand.NotifyCanExecuteChanged();
        AutoTestedCommand.NotifyCanExecuteChanged();
        FullTuningCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
    }

    public bool IsNotBusy => !IsBusy;
    public double ProgressValue
    {
        get => _progressValue;
        private set
        {
            if (Math.Abs(_progressValue - value) > 0.01)
            {
                _progressValue = value;
                OnPropertyChanged();
            }
        }
    }
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (!string.Equals(_statusText, value, StringComparison.Ordinal))
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }
    public string LivePingText
    {
        get => _livePingText;
        private set
        {
            if (!string.Equals(_livePingText, value, StringComparison.Ordinal))
            {
                _livePingText = value;
                OnPropertyChanged();
            }
        }
    }
    public string LiveDownloadText
    {
        get => _liveDownloadText;
        private set
        {
            if (!string.Equals(_liveDownloadText, value, StringComparison.Ordinal))
            {
                _liveDownloadText = value;
                OnPropertyChanged();
            }
        }
    }
    public NetworkTestReport? BeforeTest
    {
        get => _beforeTest;
        private set
        {
            _beforeTest = value;
            OnPropertyChanged();
            RefreshComparison();
        }
    }
    public NetworkTestReport? AfterTest
    {
        get => _afterTest;
        private set
        {
            _afterTest = value;
            OnPropertyChanged();
            RefreshComparison();
        }
    }

    public string DownloadBeforeText => BeforeTest?.DownloadDisplay ?? "--";
    public string DownloadAfterText => AfterTest?.DownloadDisplay ?? "--";
    public string UploadBeforeText => BeforeTest?.UploadDisplay ?? "--";
    public string UploadAfterText => AfterTest?.UploadDisplay ?? "--";
    public string LatencyBeforeText => BeforeTest?.LatencyDisplay ?? "--";
    public string LatencyAfterText => AfterTest?.LatencyDisplay ?? "--";
    public string JitterBeforeText => BeforeTest?.JitterDisplay ?? "--";
    public string JitterAfterText => AfterTest?.JitterDisplay ?? "--";

    public string DownloadDeltaText => ComputeDeltaText(BeforeTest?.DownloadSucceeded is true ? BeforeTest.DownloadMbps : null,
        AfterTest?.DownloadSucceeded is true ? AfterTest.DownloadMbps : null, higherIsBetter: true);
    public string DownloadDeltaColor => ComputeDeltaColor(BeforeTest?.DownloadSucceeded is true ? BeforeTest.DownloadMbps : null,
        AfterTest?.DownloadSucceeded is true ? AfterTest.DownloadMbps : null, higherIsBetter: true);
    public string UploadDeltaText => ComputeDeltaText(BeforeTest?.UploadSucceeded is true ? BeforeTest.UploadMbps : null,
        AfterTest?.UploadSucceeded is true ? AfterTest.UploadMbps : null, higherIsBetter: true);
    public string UploadDeltaColor => ComputeDeltaColor(BeforeTest?.UploadSucceeded is true ? BeforeTest.UploadMbps : null,
        AfterTest?.UploadSucceeded is true ? AfterTest.UploadMbps : null, higherIsBetter: true);
    public string LatencyDeltaText => ComputeDeltaText(BeforeTest?.LatencyMs, AfterTest?.LatencyMs, higherIsBetter: false);
    public string LatencyDeltaColor => ComputeDeltaColor(BeforeTest?.LatencyMs, AfterTest?.LatencyMs, higherIsBetter: false);
    public string JitterDeltaText => ComputeDeltaText(BeforeTest?.JitterMs, AfterTest?.JitterMs, higherIsBetter: false);
    public string JitterDeltaColor => ComputeDeltaColor(BeforeTest?.JitterMs, AfterTest?.JitterMs, higherIsBetter: false);

    private static string ComputeDeltaText(double? before, double? after, bool higherIsBetter)
    {
        if (before is not > 0 || after is not > 0)
        {
            return "--";
        }

        double pct = (after.Value - before.Value) / before.Value * 100.0;
        if (Math.Abs(pct) < 1.0)
        {
            return "≈ 0 %";
        }

        string sign = pct > 0 ? "+" : string.Empty;
        return $"{sign}{pct:F1} %";
    }

    private static string ComputeDeltaColor(double? before, double? after, bool higherIsBetter)
    {
        if (before is not > 0 || after is not > 0)
        {
            return "#94A3B8";
        }

        double pct = (after.Value - before.Value) / before.Value * 100.0;
        if (Math.Abs(pct) < 1.0)
        {
            return "#94A3B8";
        }

        return (higherIsBetter ? pct > 0 : pct < 0) ? "#10B981" : "#EF4444";
    }

    private void RefreshComparison()
    {
        OnPropertyChanged(nameof(DownloadBeforeText));
        OnPropertyChanged(nameof(DownloadAfterText));
        OnPropertyChanged(nameof(UploadBeforeText));
        OnPropertyChanged(nameof(UploadAfterText));
        OnPropertyChanged(nameof(LatencyBeforeText));
        OnPropertyChanged(nameof(LatencyAfterText));
        OnPropertyChanged(nameof(JitterBeforeText));
        OnPropertyChanged(nameof(JitterAfterText));
        OnPropertyChanged(nameof(DownloadDeltaText));
        OnPropertyChanged(nameof(DownloadDeltaColor));
        OnPropertyChanged(nameof(UploadDeltaText));
        OnPropertyChanged(nameof(UploadDeltaColor));
        OnPropertyChanged(nameof(LatencyDeltaText));
        OnPropertyChanged(nameof(LatencyDeltaColor));
        OnPropertyChanged(nameof(JitterDeltaText));
        OnPropertyChanged(nameof(JitterDeltaColor));
    }

    private void SafeInvokeOnDispatcher(Action action)
    {
        Application? app = Application.Current;
        if (app == null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = app.Dispatcher.InvokeAsync(action);
        }
    }

    private void AddLog(string message)
    {
        SafeInvokeOnDispatcher(() =>
        {
            OutputLog.Add(message);
            while (OutputLog.Count > MaxLogEntries)
            {
                OutputLog.RemoveAt(0);
            }
        });
    }

    public async Task RefreshAsync()
    {
        try
        {
            IReadOnlyList<NetworkAdapterInfo> adapters = await _netService.GetNetworkAdaptersAsync().ConfigureAwait(false);

            SafeInvokeOnDispatcher(() =>
            {
                Adapters.Clear();
                foreach (NetworkAdapterInfo a in adapters)
                {
                    Adapters.Add(a);
                }

                if (SelectedAdapter == null)
                {
                    SelectedAdapter = Adapters.FirstOrDefault(a => a.IsActive) ?? Adapters.FirstOrDefault();
                }
            });

            await LoadTuningStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.RefreshAsync");
        }
    }

    private async Task LoadTuningStatusAsync()
    {
        try
        {
            string? adapterId = SelectedAdapter?.Id;
            IReadOnlyList<NetworkTuningParameter> status = await _netService.GetTuningStatusAsync(adapterId).ConfigureAwait(false);

            SafeInvokeOnDispatcher(() =>
            {
                TuningParameters.Clear();
                foreach (NetworkTuningParameter p in status)
                {
                    p.DisplayName = Loc.Get(p.NameKey);
                    p.Description = Loc.Get(p.DescriptionKey);
                    p.CategoryLabel = p.Category switch
                    {
                        NetworkTuningCategory.TcpStack => Loc.Get("Network_CatTcp"),
                        _ => Loc.Get("Network_CatAdapter")
                    };
                    p.StatusText = p.IsOptimal ? Loc.Get("Network_StatusOk") : Loc.Get("Network_StatusTodo");
                    p.StatusColorHex = p.IsOptimal ? "#10B981" : "#F59E0B";
                    TuningParameters.Add(p);
                }

                OnPropertyChanged(nameof(TuningParameters));
            });
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.LoadTuningStatusAsync");
        }
    }

    public async Task RunTestAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        ProgressValue = 0;
        StatusText = Loc.Get("Network_TestRunning");
        AddLog(Loc.Get("Network_TestStarted"));

        try
        {
            NetworkTestReport report = await _netService.RunCompleteTestAsync(
                4,
                (stage, run) => AddLog(string.Equals(stage, "down"
, StringComparison.Ordinal) ? Loc.Format("Network_TestRun", run, Loc.Get("Network_MetricDownload"))
                    : Loc.Format("Network_TestRun", run, Loc.Get("Network_MetricUpload"))),
                p => ProgressValue = p,
                _cts.Token).ConfigureAwait(false);

            BeforeTest = report;
            AfterTest = null;
            StatusText = Loc.Format("Network_TestDone", report.DownloadDisplay, report.UploadDisplay, report.LatencyDisplay);
            AddLog($"↓ {report.DownloadDisplay} · ↑ {report.UploadDisplay} · {report.LatencyDisplay} · {report.JitterDisplay}");
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.Get("Network_Cancelled");
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.RunTestAsync");
            StatusText = Loc.Get("Network_TestError");
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
        }
    }

    private async Task RunTuningAsync(NetworkTuningMode mode)
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        ProgressValue = 0;
        bool isAuto = mode == NetworkTuningMode.AutoTested;
        StatusText = isAuto ? Loc.Get("Network_AutoTestedRunning") : Loc.Get("Network_FullRunning");
        AddLog(isAuto ? Loc.Get("Network_AutoTestedStarted") : Loc.Get("Network_FullStarted"));

        try
        {
            NetworkTuningReport report = await _netService.ApplyTuningAsync(
                mode,
                SelectedAdapter?.Id,
                AddLog,
                p => ProgressValue = p,
                _cts.Token).ConfigureAwait(false);

            BeforeTest = report.Before;
            AfterTest = report.After;
            StatusText = report.Summary;
            AddLog(report.Summary);

            await LoadTuningStatusAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.Get("Network_Cancelled");
            AddLog(Loc.Get("Network_Cancelled"));
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.RunTuningAsync");
            StatusText = Loc.Get("Network_TuningError");
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
        }
    }

    private async Task RestoreAsync()
    {
        IsBusy = true;
        StatusText = Loc.Get("Network_RestoreRunning");
        AddLog(Loc.Get("Network_RestoreStarted"));

        try
        {
            bool ok = await _netService.RestoreInitialValuesAsync(SelectedAdapter?.Id, AddLog).ConfigureAwait(false);
            StatusText = ok ? Loc.Get("Network_RestoreDone") : Loc.Get("Network_RestoreNone");
            AddLog(StatusText);
            await LoadTuningStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "NetworkOptimizerViewModel.RestoreAsync");
            StatusText = Loc.Get("Network_TuningError");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (_packetFilter != null) _packetFilter.StatsChanged -= OnShieldStats;
        _cts?.Cancel();
        _cts?.Dispose();
        _monitorSubscription?.Dispose();
        GC.SuppressFinalize(this);
    }
}
