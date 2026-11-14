using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using Coclico.Services;
using Microsoft.Win32;

namespace Coclico.Modules.TelemetryBlocker.Services;

internal enum TelemetryProfile
{
    Recommended
}

internal sealed class TelemetryActionItem
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Label { get; set; } = string.Empty;
}

internal sealed class RegistrySnapshot
{
    public string Path { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public bool Existed { get; set; }
    public int KindCode { get; set; }
    public string? Value { get; set; }
}

internal sealed class ServiceSnapshot
{
    public string Name { get; set; } = string.Empty;
    public bool Existed { get; set; }
    public string StartMode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

internal sealed class TaskSnapshot
{
    public string Path { get; set; } = string.Empty;
    public bool Existed { get; set; }
    public bool Enabled { get; set; }
}

internal sealed class TelemetryBackup
{
    public DateTime CreatedUtc { get; set; }
    public List<RegistrySnapshot> Registry { get; set; } = new();
    public List<ServiceSnapshot> Services { get; set; } = new();
    public List<TaskSnapshot> Tasks { get; set; } = new();
    public string HostsPath { get; set; } = string.Empty;
    public string HostsContent { get; set; } = string.Empty;
}

internal sealed class ActionResult
{
    public string ActionId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Detail { get; set; } = string.Empty;
}

internal sealed class ApplyReport
{
    public string BackupPath { get; set; } = string.Empty;
    public List<ActionResult> Results { get; set; } = new();
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailCount => Results.Count(r => !r.Success);
}

internal sealed class TelemetryBlockService
{
    public const string BackupFileName = "telemetry-backup.json";
    public const string HostsMarkerBegin = "# >>> Coclico TelemetryBlocker";
    public const string HostsMarkerEnd = "# <<< Coclico TelemetryBlocker";
    public const string HostsAdsMarkerBegin = "# >>> Coclico AdBlock (pubs & traqueurs, niveau système)";
    public const string HostsAdsMarkerEnd = "# <<< Coclico AdBlock";
    public const string DnsLockRulePrefix = "Coclico-DnsLock-";

    public static readonly string[] Categories = ["Registry", "Services", "ScheduledTasks", "Hosts", "AdsHosts", "DnsLockdown"];
    public static readonly string[] DnsResolverIps =
    [
        "8.8.8.8", "8.8.4.4", "1.1.1.1", "1.0.0.1", "9.9.9.9", "149.112.112.112",
        "208.67.222.222", "208.67.220.220", "76.76.19.19", "76.223.122.150",
        "94.140.14.14", "94.140.15.15", "94.130.180.1", "94.130.180.2", "156.154.70.1", "156.154.71.1"
    ];
    public static readonly string[] DohDomains =
    [
        "dns.google", "cloudflare-dns.com", "dns.quad9.net", "doh.opendns.com",
        "dns.adguard-dns.com", "doh.mullvad.net", "dns.alidns.com", "doh.360.cn",
        "dns.yandex.net", "common.dot.dns.yandex.net", "dns.sb", "doh.dns.sb",
        "dnsforge.de", "doh.libredns.gr", "dns.switch.ch", "doh.cleanbrowsing.org"
    ];
    public static string[] GetAdHosts()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string h in AdHosts) set.Add(h);
        foreach (string h in DohDomains) set.Add(h);
        foreach (string h in TrackerDomainCatalog.Domains) set.Add(h); 
        foreach (string h in BlocklistUpdaterService.LoadCached()) set.Add(h);
        return set.ToArray();
    }

    public static string BackupDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico");

    public static string BackupPath => Path.Combine(BackupDir, BackupFileName);

    public static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    private static readonly string[] TelemetryHosts =
    [
        "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com",
        "eu.vortex-win.data.microsoft.com",
        "de.vortex-win.data.microsoft.com",
        "asimov.vortex.data.microsoft.com",
        "bn2.vortex.data.microsoft.com.akadns.net",
        "cy2.vortex.data.microsoft.com.akadns.net",
        "geo.vortex.data.microsoft.com.akadns.net",
        "db5.vortex.data.microsoft.com.akadns.net",
        "telemetry.microsoft.com",
        "alpha.telemetry.microsoft.com",
        "ca.telemetry.microsoft.com",
        "df.telemetry.microsoft.com",
        "oca.telemetry.microsoft.com",
        "sqm.telemetry.microsoft.com",
        "watson.telemetry.microsoft.com",
        "big.telemetry.microsoft.com",
        "clouddc2.telemetry.microsoft.com",
        "sqm.microsoft.com",
        "watson.microsoft.com",
        "events.data.microsoft.com",
        "v10.events.data.microsoft.com",
        "v20.events.data.microsoft.com",
        "eu-v10.events.data.microsoft.com",
        "eu-v20.events.data.microsoft.com",
        "de-v20.events.data.microsoft.com",
        "browser.events.data.microsoft.com",
        "mobile.events.data.microsoft.com",
        "umwatson.events.data.microsoft.com",
        "settings-win.data.microsoft.com",
        "settings.data.microsoft.com",
        "db5.settings-win.data.microsoft.com.akadns.net",
        "cy2.settings.data.microsoft.com.akadns.net",
        "asimov-win.settings.data.microsoft.com.akadns.net",
        "geo.settings-win.data.microsoft.com.akadns.net",
        "activity.windows.com",
        "data.microsoft.com",
        "browser.pipe.aria.microsoft.com",
        "diagnostics.support.microsoft.com",
        "ceuswatcab01.blob.core.windows.net",
        "ceuswatcab02.blob.core.windows.net",
        "weuswatcab01.blob.core.windows.net",
        "weuswatcab02.blob.core.windows.net",
        "eaus2watcab01.blob.core.windows.net",
        "eaus2watcab02.blob.core.windows.net",
        "client-office365-tas.msedge.net",
        "augloop.office.com",
        "clientlog.portal.office.com",
        "fp.measure.office.com",
        "config.fp.measure.office.com",
    ];

    private static readonly string[] AdHosts =
    [
        "doubleclick.net", "googlesyndication.com", "google-analytics.com", "analytics.google.com",
        "googletagmanager.com", "googletagservices.com", "adservice.google.com", "pagead2.googlesyndication.com",
        "tpc.googlesyndication.com", "ad.doubleclick.net", "fls.doubleclick.net", "securepubads.g.doubleclick.net",
        "googleadservices.com", "www.googleadservices.com", "admob.com", "appspot.com",
        "connect.facebook.net", "graph.facebook.com", "pixel.facebook.com", "ads.facebook.com",
        "analytics.facebook.com", "adsmanager.facebook.com",
        "bat.bing.com", "c.bing.com", "ads.bing.com", "bat.rubiconproject.com",
        "adnxs.com", "advertising.com", "adform.net", "adroll.com", "criteo.com", "criteo.net",
        "scorecardresearch.com", "quantserve.com", "hotjar.com", "mixpanel.com", "chartbeat.com",
        "fullstory.com", "clarity.ms", "matomo.cloud", "segment.io", "cdn.segment.com", "amplitude.com",
        "snowplowanalytics.com", "newrelic.com", "nr-data.net", "sentry.io", "browser.sentry-cdn.com",
        "taboola.com", "outbrain.com", "zedo.com", "amazon-adsystem.com", "ads.linkedin.com",
        "smartadserver.com", "pubmatic.com", "rubiconproject.com", "openx.net", "casalemedia.com",
        "contextweb.com", "liadm.com", "rfihub.com", "sharethrough.com", "admeld.com", "bidswitch.net",
        "sonobi.com", "agkn.com", "mathtag.com", "pippio.com", "socdm.com", "everesttech.net",
        "rlcdn.com", "adkernel.com", "admanxs.com", "revcontent.com", "3lift.com", "eyeota.net",
        "exelator.com", "bluekai.com", "demdex.net", "krxd.net", "tapad.com", "adtech.de", "adition.com",
        "ads.yahoo.com", "analytics.yahoo.com", "adsafeprotected.com", "moatads.com", "steelhousemedia.com",
        "telemetry.instagram.com", "analytics.twitter.com", "ads.twitter.com", "ads.x.com",
        "appsflyer.com", "adjust.com", "kochava.com", "branch.io", "urbanairship.com", "pushwoosh.com",
        "onesignal.com", "braze.com", "iterable.com", "clevertap.com", "moengage.com",
        "popads.net", "popcash.net", "adsterra.com", "propellerads.com", "exoclick.com", "clickadu.com",
        "adf.ly", "bc.vc", "shorte.st",
        "coinhive.com", "crypto-loot.com", "minero.cc", "webminepool.com", "coinpot.co", "jsecoin.com",
        "authedmine.com", "webminers.com", "deepminer.com", "webmine.cz",
        "fingerprintjs.com", "fpjs.io", "maxmind.com", "browserleaks.com",
    ];

    private static readonly (string Path, string Name, string Value, bool DWord)[] RecommendedRegistry =
    [
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", "1", true),
        (@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", "1", true),
        (@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", "0", true),
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", "0", true),
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", "0", true),
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", "1", true),
        (@"HKCU\SOFTWARE\Microsoft\Siuf\Rules", "PeriodInNanoSeconds", "0", true),
        (@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", "DODownloadMode", "0", true),
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", "1", true),
        (@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", "0", true),
        (@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", "0", true),
        (@"HKCU\SOFTWARE\Microsoft\Input\TIPC", "Enabled", "0", true),
    ];

    private static readonly string[] RecommendedServices = ["DiagTrack", "dmwappushservice", "WerSvc"];

    private static readonly string[] RecommendedTasks =
    [
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Application Experience\AitAgent",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\Autochk\Proxy",
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
        @"\Microsoft\Windows\Windows Error Reporting\QueueReporting",
        @"\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
        @"\Microsoft\Windows\Application Experience\StartupAppraiser",
        @"\Microsoft\Windows\Device Information\Device",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
    ];

    public static List<TelemetryActionItem> BuildPlan(TelemetryProfile profile, ISet<string>? enabledCategories)
    {
        var plan = new List<TelemetryActionItem>();
        enabledCategories ??= new HashSet<string>(Categories, StringComparer.OrdinalIgnoreCase);

        if (enabledCategories.Contains("Registry"))
        {
            foreach (var (path, name, value, dword) in RecommendedRegistry)
            {
                plan.Add(new TelemetryActionItem
                {
                    Id = "reg:" + path + "!" + name,
                    Category = "Registry",
                    Kind = "registry",
                    Target = path + "!" + name,
                    Value = value,
                    Label = path + " -> " + name + " = " + value
                });
            }
        }

        if (enabledCategories.Contains("Services"))
        {
            foreach (string svc in RecommendedServices)
            {
                plan.Add(new TelemetryActionItem
                {
                    Id = "svc:" + svc,
                    Category = "Services",
                    Kind = "service",
                    Target = svc,
                    Value = "Disabled",
                    Label = "Service " + svc + " -> Disabled"
                });
            }
        }

        if (enabledCategories.Contains("ScheduledTasks"))
        {
            foreach (string task in RecommendedTasks)
            {
                plan.Add(new TelemetryActionItem
                {
                    Id = "task:" + task,
                    Category = "ScheduledTasks",
                    Kind = "task",
                    Target = task,
                    Value = "Disabled",
                    Label = "Tâche " + task + " -> désactivée"
                });
            }
        }

        if (enabledCategories.Contains("Hosts"))
        {
            plan.Add(new TelemetryActionItem
            {
                Id = "hosts:telemetry",
                Category = "Hosts",
                Kind = "hosts",
                Target = HostsPath,
                Value = TelemetryHosts.Length + " hôtes",
                Label = "Blocage de " + TelemetryHosts.Length + " hôtes de télémétrie dans le fichier hosts"
            });
        }

        if (enabledCategories.Contains("AdsHosts"))
        {
            plan.Add(new TelemetryActionItem
            {
                Id = "hosts:ads",
                Category = "AdsHosts",
                Kind = "hosts-ads",
                Target = HostsPath,
                Value = "liste fusionnée",
                Label = "Blocage système des pubs/traqueurs dans le fichier hosts (liste embarquée + HaGeZi/OISD en cache + DoH), pour toutes les applications, avant la carte réseau"
            });
        }

        if (enabledCategories.Contains("DnsLockdown"))
        {
            plan.Add(new TelemetryActionItem
            {
                Id = "dns:lockdown",
                Category = "DnsLockdown",
                Kind = "dns-lockdown",
                Target = "Pare-feu Windows",
                Value = "DoT 853 + IPs résolveurs DoH/DoQ bloquées",
                Label = "Verrouillage DNS : bloque DoT (TCP 853) et les résolveurs DoH/DoQ connus au pare-feu, pour que le fichier hosts ne soit pas contourné"
            });
        }

        var byId = new Dictionary<string, TelemetryActionItem>(StringComparer.Ordinal);
        foreach (var action in plan)
        {
            byId[action.Id] = action;
        }

        return byId.Values.ToList();
    }

    public static bool IsAdministrator()
    {
        try
        {
            using WindowsIdentity id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public async Task<TelemetryBackup> CaptureBackupAsync(TelemetryProfile profile)
    {
        var plan = BuildPlan(profile, null);
        var backup = new TelemetryBackup
        {
            CreatedUtc = DateTime.UtcNow,
            HostsPath = HostsPath,
            HostsContent = File.Exists(HostsPath) ? await File.ReadAllTextAsync(HostsPath).ConfigureAwait(false) : string.Empty
        };

        foreach (var action in plan.Where(a => string.Equals(a.Kind, "registry", StringComparison.Ordinal)))
        {
            backup.Registry.Add(ReadRegistrySnapshot(action.Target));
        }

        foreach (var action in plan.Where(a => string.Equals(a.Kind, "service", StringComparison.Ordinal)))
        {
            backup.Services.Add(ReadServiceSnapshot(action.Target));
        }

        foreach (var action in plan.Where(a => string.Equals(a.Kind, "task", StringComparison.Ordinal)))
        {
            backup.Tasks.Add(await ReadTaskSnapshotAsync(action.Target).ConfigureAwait(false));
        }

        Directory.CreateDirectory(BackupDir);
        await File.WriteAllTextAsync(BackupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        return backup;
    }

    public async Task<ApplyReport> ApplyAsync(TelemetryProfile profile, ISet<string> enabledCategories)
    {
        var plan = BuildPlan(profile, enabledCategories);
        if (!File.Exists(BackupPath))
        {
            await CaptureBackupAsync(profile).ConfigureAwait(false);
        }

        var report = new ApplyReport { BackupPath = BackupPath };
        foreach (var action in plan)
        {
            report.Results.Add(await ApplyOneAsync(action).ConfigureAwait(false));
        }

        return report;
    }

    public async Task<ApplyReport> RestoreAsync()
    {
        var report = new ApplyReport { BackupPath = BackupPath };
        if (!File.Exists(BackupPath))
        {
            report.Results.Add(new ActionResult { ActionId = "backup", Success = false, Detail = "Aucune sauvegarde trouvée." });
            return report;
        }

        var backup = JsonSerializer.Deserialize<TelemetryBackup>(await File.ReadAllTextAsync(BackupPath).ConfigureAwait(false)) ?? new TelemetryBackup();

        foreach (var snap in backup.Registry)
        {
            report.Results.Add(RestoreRegistry(snap));
        }

        foreach (var snap in backup.Services)
        {
            report.Results.Add(RestoreService(snap));
        }

        foreach (var snap in backup.Tasks)
        {
            report.Results.Add(await RestoreTaskAsync(snap).ConfigureAwait(false));
        }

        if (!string.IsNullOrEmpty(backup.HostsPath))
        {
            try
            {
                await File.WriteAllTextAsync(backup.HostsPath, backup.HostsContent).ConfigureAwait(false);
                report.Results.Add(new ActionResult { ActionId = "hosts:telemetry", Success = true, Detail = "Fichier hosts restauré." });
            }
            catch (Exception ex)
            {
                report.Results.Add(new ActionResult { ActionId = "hosts:telemetry", Success = false, Detail = ex.Message });
            }
        }


        try
        {
            int fwRemoved = RemoveCoclicoFirewallRules();
            report.Results.Add(new ActionResult { ActionId = "firewall:cleanup", Success = true, Detail = fwRemoved + " règle(s) pare-feu supprimée(s)." });
        }
        catch (Exception ex)
        {
            report.Results.Add(new ActionResult { ActionId = "firewall:cleanup", Success = false, Detail = ex.Message });
        }

        return report;
    }

    public async Task<List<(TelemetryActionItem Action, bool IsApplied, string Current)>> VerifyAsync(TelemetryProfile profile, ISet<string> enabledCategories)
    {
        var plan = BuildPlan(profile, enabledCategories);
        var results = new List<(TelemetryActionItem, bool, string)>();
        foreach (var action in plan)
        {
            var (ok, current) = action.Kind switch
            {
                "registry" => VerifyRegistry(action),
                "service" => VerifyService(action),
                "task" => await VerifyTaskAsync(action).ConfigureAwait(false),
                "hosts" => VerifyHosts(action),
                "hosts-ads" => VerifyHostsAds(),
                "dns-lockdown" => VerifyDnsLockdown(),
                _ => (false, "?")
            };
            results.Add((action, ok, current));
        }

        return results;
    }

    public async Task ReapplyIfNeededAsync(TelemetryProfile profile, ISet<string> enabledCategories)
    {
        if (!IsAdministrator())
        {
            return;
        }

        var verify = await VerifyAsync(profile, enabledCategories).ConfigureAwait(false);
        if (verify.All(v => v.IsApplied))
        {
            return;
        }

        await ApplyAsync(profile, enabledCategories).ConfigureAwait(false);
    }

    private async Task<ActionResult> ApplyOneAsync(TelemetryActionItem action)
    {
        try
        {
            return action.Kind switch
            {
                "registry" => ApplyRegistry(action),
                "service" => ApplyService(action),
                "task" => await ApplyTaskAsync(action).ConfigureAwait(false),
                "hosts" => await ApplyHostsAsync().ConfigureAwait(false),
                "hosts-ads" => await ApplyHostsAdsAsync().ConfigureAwait(false),
                "dns-lockdown" => ApplyDnsLockdown(),
                _ => new ActionResult { ActionId = action.Id, Success = false, Detail = "Kind inconnu" }
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { ActionId = action.Id, Success = false, Detail = ex.Message };
        }
    }

    private static (RegistryKey Base, string SubKey, string Name) SplitRegistryTarget(string target)
    {
        int bang = target.IndexOf('!');
        string path = target[..bang];
        string name = target[(bang + 1)..];
        int sep = path.IndexOf('\\');
        string hive = path[..sep];
        string sub = path[(sep + 1)..];
        RegistryKey baseKey = hive.ToUpperInvariant() switch
        {
            "HKLM" => Registry.LocalMachine,
            "HKCU" => Registry.CurrentUser,
            "HKCR" => Registry.ClassesRoot,
            _ => Registry.LocalMachine
        };
        return (baseKey, sub, name);
    }

    private static ActionResult ApplyRegistry(TelemetryActionItem action)
    {
        var (hive, sub, name) = SplitRegistryTarget(action.Target);
        using RegistryKey key = hive.CreateSubKey(sub, writable: true);
        if (key == null)
        {
            return new ActionResult { ActionId = action.Id, Success = false, Detail = "Impossible d'ouvrir la clé." };
        }

        if (int.TryParse(action.Value, System.Globalization.CultureInfo.InvariantCulture, out int intValue))
        {
            key.SetValue(name, intValue, RegistryValueKind.DWord);
        }
        else
        {
            key.SetValue(name, action.Value ?? string.Empty, RegistryValueKind.String);
        }

        return new ActionResult { ActionId = action.Id, Success = true, Detail = "OK" };
    }

    private static RegistrySnapshot ReadRegistrySnapshot(string target)
    {
        var (hive, sub, name) = SplitRegistryTarget(target);
        var snap = new RegistrySnapshot { Path = target[..target.IndexOf('!')], ValueName = name };
        try
        {
            using RegistryKey? key = hive.OpenSubKey(sub, writable: false);
            if (key?.GetValue(name) is { } val)
            {
                snap.Existed = true;
                snap.KindCode = (int)key.GetValueKind(name);
                snap.Value = key.GetValueKind(name) == RegistryValueKind.DWord ? ((int)val).ToString(System.Globalization.CultureInfo.InvariantCulture) : val as string;
            }
        }
        catch
        {
        }

        return snap;
    }

    private static ActionResult RestoreRegistry(RegistrySnapshot snap)
    {
        try
        {
            var (hive, sub, name) = SplitRegistryTarget(snap.Path + "!" + snap.ValueName);
            using RegistryKey key = hive.CreateSubKey(sub, writable: true);
            if (key == null)
            {
                return new ActionResult { ActionId = "reg:" + snap.Path + "!" + snap.ValueName, Success = false, Detail = "Clé introuvable." };
            }

            if (snap.Existed)
            {
                var kind = (RegistryValueKind)snap.KindCode;
                if (kind == RegistryValueKind.DWord && int.TryParse(snap.Value, System.Globalization.CultureInfo.InvariantCulture, out int iv))
                {
                    key.SetValue(name, iv, RegistryValueKind.DWord);
                }
                else
                {
                    key.SetValue(name, snap.Value ?? string.Empty, kind == RegistryValueKind.DWord ? RegistryValueKind.DWord : RegistryValueKind.String);
                }
            }
            else
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }

            return new ActionResult { ActionId = "reg:" + snap.Path + "!" + snap.ValueName, Success = true, Detail = "Restauré." };
        }
        catch (Exception ex)
        {
            return new ActionResult { ActionId = "reg:" + snap.Path + "!" + snap.ValueName, Success = false, Detail = ex.Message };
        }
    }

    private static (bool Existed, string StartMode, string State) ReadService(string name)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher($"SELECT StartMode, State FROM Win32_Service WHERE Name = '{name}'");
            using var results = searcher.Get();
            foreach (System.Management.ManagementObject mo in results)
            {
                return (true, mo["StartMode"]?.ToString() ?? "", mo["State"]?.ToString() ?? "");
            }
        }
        catch
        {
        }

        return (false, "", "");
    }

    private static ServiceSnapshot ReadServiceSnapshot(string name)
    {
        var (existed, startMode, state) = ReadService(name);
        return new ServiceSnapshot { Name = name, Existed = existed, StartMode = startMode, State = state };
    }

    private static ActionResult ApplyService(TelemetryActionItem action)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher($"SELECT * FROM Win32_Service WHERE Name = '{action.Target}'");
            using var results = searcher.Get();
            foreach (System.Management.ManagementObject mo in results)
            {
                if (string.Equals(mo["State"]?.ToString(), "Running", StringComparison.OrdinalIgnoreCase))
                {
                    mo.InvokeMethod("StopService", null);
                }

                mo.InvokeMethod("ChangeStartMode", new object[] { "Disabled" });
                mo.Dispose();
                return new ActionResult { ActionId = action.Id, Success = true, Detail = "Service arrêté et désactivé." };
            }

            return new ActionResult { ActionId = action.Id, Success = false, Detail = "Service introuvable." };
        }
        catch (Exception ex)
        {
            return new ActionResult { ActionId = action.Id, Success = false, Detail = ex.Message };
        }
    }

    private static ActionResult RestoreService(ServiceSnapshot snap)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher($"SELECT * FROM Win32_Service WHERE Name = '{snap.Name}'");
            using var results = searcher.Get();
            foreach (System.Management.ManagementObject mo in results)
            {
                string startMode = string.IsNullOrEmpty(snap.StartMode) ? "Manual" : snap.StartMode;
                mo.InvokeMethod("ChangeStartMode", new object[] { startMode });
                if (string.Equals(snap.State, "Running", StringComparison.OrdinalIgnoreCase))
                {
                    mo.InvokeMethod("StartService", null);
                }

                mo.Dispose();
                return new ActionResult { ActionId = "svc:" + snap.Name, Success = true, Detail = "Restauré (" + startMode + ")." };
            }

            return new ActionResult { ActionId = "svc:" + snap.Name, Success = false, Detail = "Service introuvable." };
        }
        catch (Exception ex)
        {
            return new ActionResult { ActionId = "svc:" + snap.Name, Success = false, Detail = ex.Message };
        }
    }

    private static (int Code, string Output) RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null)
        {
            return (-1, "process null");
        }

        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(30000);
        return (p.ExitCode, output);
    }

    private static async Task<TaskSnapshot> ReadTaskSnapshotAsync(string path)
    {
        var snap = new TaskSnapshot { Path = path };
        await Task.Run(() =>
        {
            var (code, output) = RunProcess("schtasks.exe", $"/Query /TN \"{path}\" /FO LIST /V");
            if (code == 0)
            {
                snap.Existed = true;
                foreach (var line in output.Split('\n'))
                {
                    if (line.TrimStart().StartsWith("Enabled:", StringComparison.OrdinalIgnoreCase))
                    {
                        snap.Enabled = line.Contains("Yes", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }).ConfigureAwait(false);
        return snap;
    }

    private static async Task<ActionResult> ApplyTaskAsync(TelemetryActionItem action)
    {
        var snap = await ReadTaskSnapshotAsync(action.Target).ConfigureAwait(false);
        if (!snap.Existed)
        {
            return new ActionResult { ActionId = action.Id, Success = true, Detail = "Tâche absente du système (déjà conforme)." };
        }

        return await Task.Run(() =>
        {
            var (code, output) = RunProcess("schtasks.exe", $"/Change /TN \"{action.Target}\" /DISABLE");
            bool missing = code != 0 && IsTaskMissingOutput(output);
            return new ActionResult
            {
                ActionId = action.Id,
                Success = code == 0 || missing,
                Detail = code == 0 ? "Tâche désactivée." : missing ? "Tâche absente du système (déjà conforme)." : output.Trim()
            };
        }).ConfigureAwait(false);
    }

    private static bool IsTaskMissingOutput(string output)
    {
        if (string.IsNullOrEmpty(output)) return false;
        string o = output.ToLowerInvariant();
        return o.Contains("n'existe pas") || o.Contains("cannot find") || o.Contains("not found") || o.Contains("introuvable");
    }

    private static async Task<ActionResult> RestoreTaskAsync(TaskSnapshot snap)
    {
        if (!snap.Existed)
        {
            return new ActionResult { ActionId = "task:" + snap.Path, Success = true, Detail = "Tâche absente (rien à restaurer)." };
        }

        return await Task.Run(() =>
        {
            string arg = snap.Enabled ? "/ENABLE" : "/DISABLE";
            var (code, output) = RunProcess("schtasks.exe", $"/Change /TN \"{snap.Path}\" {arg}");
            bool missing = code != 0 && IsTaskMissingOutput(output);
            return new ActionResult
            {
                ActionId = "task:" + snap.Path,
                Success = code == 0 || missing,
                Detail = code == 0 ? "Tâche restaurée." : missing ? "Tâche absente (rien à restaurer)." : output.Trim()
            };
        }).ConfigureAwait(false);
    }

    private static async Task<(bool IsApplied, string Current)> VerifyTaskAsync(TelemetryActionItem action)
    {
        var snap = await ReadTaskSnapshotAsync(action.Target).ConfigureAwait(false);
        if (!snap.Existed)
        {
            return (true, "absente");
        }

        return (!snap.Enabled, snap.Enabled ? "activée" : "désactivée");
    }

    private static (bool, string) VerifyRegistry(TelemetryActionItem action)
    {
        var snap = ReadRegistrySnapshot(action.Target);
        if (!snap.Existed)
        {
            return (false, "absente");
        }

        return (string.Equals(snap.Value, action.Value, StringComparison.OrdinalIgnoreCase), snap.Value ?? "");
    }

    private static (bool, string) VerifyService(TelemetryActionItem action)
    {
        var (_, startMode, state) = ReadService(action.Target);
        bool ok = string.Equals(startMode, "Disabled", StringComparison.OrdinalIgnoreCase)
                  && !string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase);
        return (ok, $"{startMode}/{state}");
    }

    private static (bool, string) VerifyHosts(TelemetryActionItem action)
    {
        try
        {
            if (!File.Exists(HostsPath))
            {
                return (false, "fichier hosts absent");
            }

            string content = File.ReadAllText(HostsPath);
            bool ok = content.Contains(HostsMarkerBegin) && TelemetryHosts.All(h => content.Contains(h));
            return (ok, ok ? "bloc présent" : "bloc incomplet");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<ActionResult> ApplyHostsAsync()
    {
        return await WriteHostsBlockAsync(HostsMarkerBegin, HostsMarkerEnd, TelemetryHosts, "hosts:telemetry").ConfigureAwait(false);
    }

    private static async Task<ActionResult> ApplyHostsAdsAsync()
    {
        string[] hosts = GetAdHosts();
        return await WriteHostsBlockAsync(HostsAdsMarkerBegin, HostsAdsMarkerEnd, hosts, "hosts:ads").ConfigureAwait(false);
    }

    private static ActionResult ApplyDnsLockdown()
    {
        try
        {
            var results = new List<(bool Ok, string Detail)>();
            results.Add(RunNetshOk($"advfirewall firewall add rule name=\"{DnsLockRulePrefix}DoT-853\" dir=out action=block protocol=TCP remoteport=853 profile=any", "DoT 853 bloqué"));
            string ips = string.Join(",", DnsResolverIps);
            results.Add(RunNetshOk($"advfirewall firewall add rule name=\"{DnsLockRulePrefix}Resolvers\" dir=out action=block protocol=ANY remoteip={ips} profile=any", "IPs résolveurs DoH/DoQ bloquées"));

            bool ok = results.All(r => r.Ok);
            return new ActionResult
            {
                ActionId = "dns:lockdown",
                Success = ok,
                Detail = ok ? "DNS verrouillé (DoT 853 + résolveurs DoH/DoQ bloqués)." : string.Join(" | ", results.Where(r => !r.Ok).Select(r => r.Detail))
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { ActionId = "dns:lockdown", Success = false, Detail = ex.Message };
        }
    }

    private static (bool Ok, string Detail) RunNetshOk(string args, string okDetail)
    {
        var (code, output) = RunProcess("netsh", args);
        bool ok = code == 0 || (output?.Contains("already exists", StringComparison.OrdinalIgnoreCase) ?? false);
        return (ok, ok ? okDetail : output?.Trim() ?? "erreur");
    }

    private static (bool, string) VerifyDnsLockdown()
    {
        try
        {
            var (code, output) = RunProcess("netsh", "advfirewall firewall show rule name=all");
            if (code != 0) return (false, "netsh indisponible");
            bool dot = output.Contains(DnsLockRulePrefix + "DoT-853", StringComparison.OrdinalIgnoreCase);
            bool resolvers = output.Contains(DnsLockRulePrefix + "Resolvers", StringComparison.OrdinalIgnoreCase);
            bool ok = dot && resolvers;
            return (ok, ok ? "règles présentes" : $"DoT:{dot} Résolveurs:{resolvers}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }


    public static int RemoveCoclicoFirewallRules()
    {
        int removed = 0;
        foreach (string prefix in new[] { DnsLockRulePrefix, PacketFilterService.FirewallRulePrefix })
        {
            var (code, output) = RunProcess("netsh", "advfirewall firewall show rule name=all");
            if (code != 0) continue;
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                int idx = trimmed.IndexOf(prefix, StringComparison.Ordinal);
                if (idx < 0) continue;
                int start = trimmed.LastIndexOf(' ', idx) + 1;
                string name = trimmed.Substring(start).Trim();
                if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
                {
                    var (c2, _) = RunProcess("netsh", "advfirewall firewall delete rule name=\"" + name + "\"");
                    if (c2 == 0) removed++;
                }
            }
        }
        return removed;
    }

    private static async Task<ActionResult> WriteHostsBlockAsync(string markerBegin, string markerEnd, string[] hosts, string actionId)
    {
        return await Task.Run(() =>
        {
            try
            {
                string content = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : string.Empty;
                int begin = content.IndexOf(markerBegin, StringComparison.Ordinal);
                int end = content.IndexOf(markerEnd, StringComparison.Ordinal);
                if (begin >= 0 && end > begin)
                {
                    content = content.Remove(begin, end + markerEnd.Length - begin);
                    if (begin > 0 && content[begin - 1] == '\n')
                    {
                        content = content.Remove(begin - 1, 1);
                    }
                }

                content = content.TrimEnd('\r', '\n') + "\n\n" + markerBegin + "\n";
                foreach (string host in hosts)
                {
                    content += "0.0.0.0 " + host + "\n";
                }

                content += markerEnd + "\n";
                const int maxAttempts = 6;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        using (var fs = new FileStream(HostsPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        using (var w = new StreamWriter(fs))
                        {
                            w.Write(content);
                        }
                        return new ActionResult { ActionId = actionId, Success = true, Detail = hosts.Length + " hôtes bloqués." };
                    }
                    catch (IOException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(500 * attempt);
                    }
                }

                return new ActionResult { ActionId = actionId, Success = false, Detail = "Fichier hosts verrouillé par un autre processus après " + maxAttempts + " tentatives." };
            }
            catch (Exception ex)
            {
                return new ActionResult { ActionId = actionId, Success = false, Detail = ex.Message };
            }
        }).ConfigureAwait(false);
    }

    private static (bool, string) VerifyHostsAds()
    {
        try
        {
            if (!File.Exists(HostsPath))
            {
                return (false, "fichier hosts absent");
            }

            string content = File.ReadAllText(HostsPath);
            bool ok = content.Contains(HostsAdsMarkerBegin)
                      && AdHosts.Take(10).All(h => content.Contains(h))
                      && content.Contains(DohDomains[0]);
            return (ok, ok ? "bloc présent" : "bloc incomplet");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
