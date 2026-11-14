using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Coclico.Services;

internal class StartupProgress
{
    public string Status { get; set; } = string.Empty;
    public string SubDetail { get; set; } = string.Empty;
    public int Percent { get; set; }
    public string LogEntry { get; set; } = string.Empty;
    public string? CpuInfo { get; set; }
    public string? RamInfo { get; set; }
    public string? GpuInfo { get; set; }
    public string? SysInfo { get; set; }
}

internal class StartupService
{
    public async Task RunStartupAsync(IProgress<StartupProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            await RunCoreAsync(progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LoggingService.LogInfo("Startup cancelled");
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "StartupService.RunStartupAsync");
            throw;
        }
    }

    private async Task RunCoreAsync(IProgress<StartupProgress>? progress, CancellationToken ct)
    {
        bool isAdmin = IsAdministrator();
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();

        await ServiceContainer.GetRequired<SettingsService>().LoadAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        LocalizationService? loc = ServiceContainer.GetOptional<LocalizationService>();

        Report(progress, 12,
            loc?.Get("Startup_Services") ?? "Démarrage des services...",
            loc?.Get("Startup_ServicesSub") ?? "Initialisation de l'environnement",
            loc?.Get("Startup_ServicesLog") ?? "Démarrage des services système",
            cpuInfo: $"{Environment.ProcessorCount} Cœurs • {arch}",
            sysInfo: BuildSystemInfo(isAdmin));

        Report(progress, 25,
            loc?.Get("Startup_MemOptimize") ?? "Optimisation de la mémoire...",
            loc?.Get("Startup_MemAnalyze") ?? "Analyse de la mémoire vive",
            loc?.Get("Startup_MemAnalyze") ?? "Analyse de la mémoire vive",
            ramInfo: BuildRamInfo(loc));

        TryRun(() => ServiceContainer.GetRequired<ResourceGuardService>().Start(), "StartupService.ResourceGuard");

        Report(progress, 38,
            loc?.Get("Startup_Display") ?? "Configuration de l'affichage...",
            loc?.Get("Startup_DisplaySub") ?? "Accélération graphique matérielle",
            loc?.Get("Startup_DisplayLog") ?? "Initialisation de l'affichage",
            gpuInfo: BuildGpuInfo());

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            TryRun(
                () => ServiceContainer.GetRequired<ThemeService>().ApplyCurrentSettings(),
                "StartupService.ApplyTheme");
            TryRun(
                () => ServiceContainer.GetRequired<LocalizationService>()
                    .SetLanguage(ServiceContainer.GetRequired<SettingsService>().Settings.Language),
                "StartupService.SetLanguage");
        });
        ct.ThrowIfCancellationRequested();

        Report(progress, 50,
            loc?.Get("Startup_SysServices") ?? "Vérification des services système...",
            loc?.Get("Startup_SysServicesSub") ?? "Contrôle des services Windows",
            loc?.Get("Startup_SysServicesLog") ?? "Vérification des services Windows");

        Task healthTask = Task.Run(
            () => ServiceContainer.GetRequired<StartupHealthService>().CheckAndRepairAsync());
        Task scanTask = Task.Run(async () =>
        {
            try
            {
                var programs = ServiceContainer.GetRequired<InstalledProgramsService>();
                _ = await programs.GetAllInstalledProgramsAsync(cancellationToken: ct).ConfigureAwait(false);
                List<string> iconPaths = programs.GetMemoryCacheIconPaths();
                if (iconPaths.Count > 0)
                {
                    _ = Coclico.Converters.FileIconConverter.PreloadAllAsync(iconPaths);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
            {
                LoggingService.LogException(ex, "StartupService.AppsScan");
            }
        });

        try
        {
            await healthTask.ConfigureAwait(false);
            Report(progress, 52,
                loc?.Get("Startup_ServicesOk") ?? "Services système vérifiés",
                loc?.Get("Startup_ServicesOkSub") ?? "Services Windows opérationnels",
                loc?.Get("Startup_ServicesOkLog") ?? "Services système opérationnels");
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "StartupService.HealthCheck");
        }
        ct.ThrowIfCancellationRequested();

        Report(progress, 64,
            loc?.Get("Startup_Components") ?? "Contrôle des composants...",
            loc?.Get("Startup_ComponentsSub") ?? "Modules système prêts",
            loc?.Get("Startup_ComponentsLog") ?? "Composants prêts");

        Report(progress, 78,
            loc?.Get("Startup_CheckUpdates") ?? "Recherche des mises à jour...",
            loc?.Get("Startup_VersionControl") ?? "Vérification de la version",
            loc?.Get("Startup_CheckUpdates") ?? "Recherche des mises à jour");

        Report(progress, 80,
            loc?.Get("Startup_CheckUpdates") ?? "Vérification des mises à jour...",
            "v" + UpdateCheckService.GetCurrentVersion(),
            loc?.Get("Startup_VersionControl") ?? "Contrôle de version effectué");

        Report(progress, 90,
            loc?.Get("Startup_LoadingApps") ?? "Chargement des applications...",
            loc?.Get("Startup_IndexApps") ?? "Indexation des programmes installés",
            loc?.Get("Startup_IndexApps") ?? "Indexation des programmes");

        try
        {
            await scanTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "StartupService.AppsScanAwait");
        }

        Report(progress, 98,
            loc?.Get("Startup_Finalizing") ?? "Finalisation du démarrage...",
            loc?.Get("Startup_TrimFootprint") ?? "Optimisation de l'empreinte mémoire",
            loc?.Get("Startup_Finalizing") ?? "Finalisation du démarrage");

        TryRun(MemoryCleanerService.TrimSelfWorkingSet, "StartupService.TrimWorkingSet");
        ct.ThrowIfCancellationRequested();

        Report(progress, 100,
            loc?.Get("Startup_Ready") ?? "Prêt !",
            loc?.Get("Startup_Opening") ?? "Ouverture de Coclico...",
            loc?.Get("Startup_Done") ?? "Démarrage terminé");
    }

    private static void Report(
        IProgress<StartupProgress>? progress,
        int percent,
        string status,
        string subDetail,
        string logEntry,
        string? cpuInfo = null,
        string? ramInfo = null,
        string? gpuInfo = null,
        string? sysInfo = null)
    {
        progress?.Report(new StartupProgress
        {
            Status = status,
            SubDetail = subDetail,
            Percent = percent,
            LogEntry = logEntry,
            CpuInfo = cpuInfo,
            RamInfo = ramInfo,
            GpuInfo = gpuInfo,
            SysInfo = sysInfo
        });
    }

    private static void TryRun(Action action, string context)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, context);
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using WindowsIdentity? id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "StartupService.IsAdministrator");
            return false;
        }
    }

    private static string BuildSystemInfo(bool isAdmin)
    {
        string platform = Environment.OSVersion.Version.Major >= 10 ? "11/10" : "NT";
        return $"Win {platform} • {(isAdmin ? "Admin" : "User")}";
    }

    private static string BuildRamInfo(LocalizationService? loc)
    {
        MemoryCleanerService.RamInfo ram = MemoryCleanerService.GetRamInfo();
        double availGb = ram.AvailPhysBytes / (1024.0 * 1024.0 * 1024.0);
        double totalGb = ram.TotalPhysBytes / (1024.0 * 1024.0 * 1024.0);
        return TextFormatService.Format(System.Globalization.CultureInfo.InvariantCulture, loc?.Get("Startup_RamFree") ?? "{0:F1} Go Libre / {1:F0} Go", availGb, totalGb);
    }

    private static string BuildGpuInfo()
    {
        try
        {
            MemoryCleanerService.GpuInfo gpu = MemoryCleanerService.GetGpuInfo();
            string name = string.IsNullOrWhiteSpace(gpu.Name) ? "GPU Standard" : gpu.Name;
            if (name.Length > 22)
            {
                name = name[..22] + "…";
            }

            return gpu.IsIntegrated ? $"{name} (Intégré)" : $"{name} (Dédié)";
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "StartupService.BuildGpuInfo");
            return "DirectX DWM";
        }
    }
}
