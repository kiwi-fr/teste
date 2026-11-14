using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Coclico.Modules.TelemetryBlocker.Services;
using Coclico.Services;
using CommunityToolkit.Mvvm.Input;

namespace Coclico.Modules.TelemetryBlocker.ViewModels;

internal sealed class TelemetryBlockerViewModel : INotifyPropertyChanged
{
    private readonly TelemetryBlockService _service;
    private readonly SettingsService _settings;

    public ObservableCollection<string> Log { get; } = new();

    public string[] Profiles { get; } = ["Recommended"];

    private string _selectedProfile = "Recommended";
    public string SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (string.Equals(_selectedProfile, value, StringComparison.Ordinal)) return;
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProfileIndex));
        }
    }

    public int SelectedProfileIndex
    {
        get => Array.IndexOf(Profiles, _selectedProfile);
        set
        {
            if (value >= 0 && value < Profiles.Length)
            {
                SelectedProfile = Profiles[value];
            }
        }
    }

    private bool BoolProp(string name, bool value)
    {
        OnPropertyChanged(name);
        return value;
    }

    private bool _catRegistry = true;
    public bool CatRegistry { get => _catRegistry; set => _catRegistry = BoolProp(nameof(CatRegistry), value); }

    private bool _catServices = true;
    public bool CatServices { get => _catServices; set => _catServices = BoolProp(nameof(CatServices), value); }

    private bool _catTasks = true;
    public bool CatTasks { get => _catTasks; set => _catTasks = BoolProp(nameof(CatTasks), value); }

    private bool _catHosts = true;
    public bool CatHosts { get => _catHosts; set => _catHosts = BoolProp(nameof(CatHosts), value); }

    private bool _catAdsHosts = true;
    public bool CatAdsHosts { get => _catAdsHosts; set => _catAdsHosts = BoolProp(nameof(CatAdsHosts), value); }

    private bool _catDnsLockdown = true;
    public bool CatDnsLockdown { get => _catDnsLockdown; set => _catDnsLockdown = BoolProp(nameof(CatDnsLockdown), value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            ApplyCommand.NotifyCanExecuteChanged();
            RestoreCommand.NotifyCanExecuteChanged();
            VerifyCommand.NotifyCanExecuteChanged();
        }
    }

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        set { if (string.Equals(_status, value, StringComparison.Ordinal)) return; _status = value; OnPropertyChanged(); }
    }

    public bool IsAdmin => TelemetryBlockService.IsAdministrator();

    private string _extensionStatus = string.Empty;
    public string ExtensionStatus
    {
        get => _extensionStatus;
        set { if (string.Equals(_extensionStatus, value, StringComparison.Ordinal)) return; _extensionStatus = value; OnPropertyChanged(); }
    }

    public int SelectedBrowserIndex { get; set; }

    public IRelayCommand ApplyCommand { get; }
    public IRelayCommand RestoreCommand { get; }
    public IRelayCommand VerifyCommand { get; }
    public IRelayCommand InstallExtensionCommand { get; }

    public TelemetryBlockerViewModel(TelemetryBlockService service, SettingsService settings)
    {
        _service = service;
        _settings = settings;

        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !IsBusy);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => !IsBusy);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => !IsBusy);
        InstallExtensionCommand = new AsyncRelayCommand(InstallExtensionAsync);

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        AppSettings s = _settings.Settings;
        SelectedProfile = string.IsNullOrEmpty(s.TelemetryBlockProfile) ? "Recommended" : s.TelemetryBlockProfile;
        var enabled = new HashSet<string>(
            (s.TelemetryBlockCategories ?? string.Join(",", TelemetryBlockService.Categories))
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        CatRegistry = enabled.Contains("Registry");
        CatServices = enabled.Contains("Services");
        CatTasks = enabled.Contains("ScheduledTasks");
        CatHosts = enabled.Contains("Hosts");
        CatAdsHosts = enabled.Contains("AdsHosts");
        CatDnsLockdown = enabled.Contains("DnsLockdown");

        Status = s.TelemetryBlockEnabled ? Loc.Get("Telemetry_Status_Applied") : Loc.Get("Telemetry_Status_Idle");
    }

    private HashSet<string> EnabledCategories()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (CatRegistry) set.Add("Registry");
        if (CatServices) set.Add("Services");
        if (CatTasks) set.Add("ScheduledTasks");
        if (CatHosts) set.Add("Hosts");
        if (CatAdsHosts) set.Add("AdsHosts");
        if (CatDnsLockdown) set.Add("DnsLockdown");
        return set;
    }

    private TelemetryProfile Profile => TelemetryProfile.Recommended;

    private async Task ApplyAsync()
    {
        if (!IsAdmin)
        {
            Status = Loc.Get("Telemetry_AdminRequired");
            return;
        }

        IsBusy = true;
        Log.Clear();
        try
        {
            var report = await _service.ApplyAsync(Profile, EnabledCategories()).ConfigureAwait(false);
            foreach (var r in report.Results.Where(r => !r.Success))
            {
                Log.Add("[ECHEC] " + r.ActionId + " : " + r.Detail);
            }

            Status = $"{report.SuccessCount}/{report.Results.Count} actions appliquées. Sauvegarde : {report.BackupPath}";
            var hostsAds = report.Results.FirstOrDefault(r => string.Equals(r.ActionId, "hosts:ads", StringComparison.Ordinal) && r.Success);
            if (hostsAds != null) Status += $" — Blocage système actif : {hostsAds.Detail}";
            var dnsLock = report.Results.FirstOrDefault(r => string.Equals(r.ActionId, "dns:lockdown", StringComparison.Ordinal) && r.Success);
            if (dnsLock != null) Status += $" — {dnsLock.Detail}";
            _settings.Settings.TelemetryBlockEnabled = true;
            _settings.Settings.TelemetryBlockProfile = SelectedProfile;
            _settings.Settings.TelemetryBlockCategories = string.Join(",", EnabledCategories());
            _settings.Save();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreAsync()
    {
        if (!IsAdmin)
        {
            Status = Loc.Get("Telemetry_AdminRequired");
            return;
        }

        IsBusy = true;
        try
        {
            var report = await _service.RestoreAsync().ConfigureAwait(false);
            Status = report.FailCount == 0
                ? Loc.Get("Telemetry_Status_Restored")
                : $"{report.FailCount} échec(s) à la restauration.";
            _settings.Settings.TelemetryBlockEnabled = false;
            _settings.Save();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task VerifyAsync()
    {
        IsBusy = true;
        try
        {
            var results = await _service.VerifyAsync(Profile, EnabledCategories()).ConfigureAwait(false);
            int ok = results.Count(r => r.IsApplied);
            Status = $"{ok}/{results.Count} actions vérifiées comme appliquées.";
            var hostsAds = results.FirstOrDefault(r => string.Equals(r.Action.Id, "hosts:ads", StringComparison.Ordinal));
            if (hostsAds.IsApplied) Status += " — Bloc hosts pubs/traqueurs présent (" + hostsAds.Current + ").";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task InstallExtensionAsync()
    {
        try
        {
            string dir = new BrowserExtensionService().DeployExtension();
            var browser = SelectedBrowserIndex switch
            {
                1 => BrowserKind.Edge,
                2 => BrowserKind.Firefox,
                3 => BrowserKind.Brave,
                4 => BrowserKind.Zen,
                _ => BrowserKind.Chrome
            };
            string? exe = BrowserExtensionService.FindBrowserExe(browser);
            bool gecko = browser is BrowserKind.Firefox or BrowserKind.Zen;
            var (opened, detail) = BrowserExtensionService.OpenExtensionsPage(browser, exe);
            ExtensionStatus = opened
                ? Loc.Format(gecko ? "Telemetry_Ext_OpenedGecko" : "Telemetry_Ext_Opened", dir, detail)
                : Loc.Format("Telemetry_Ext_Manual", dir);
        }
        catch (Exception ex)
        {
            ExtensionStatus = ex.Message;
        }

        return Task.CompletedTask;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
