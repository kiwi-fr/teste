namespace Coclico.Modules.Power.Services;

internal sealed record PowerBenchmarkResult(CoclicoPowerMode Mode, double Score);

internal sealed record PowerBenchmarkStep(CoclicoPowerMode Mode, string PlanName, double Score, double ProgressPercent);

internal static class PowerBenchmarkOrchestrator
{
    public static async Task<List<PowerBenchmarkResult>> RunAsync(
        CpuPowerService cpuPower,
        IProgress<PowerBenchmarkStep> progress,
        IProgress<string> status,
        CancellationToken ct = default)
    {
        _ = await cpuPower.EnsureCoclicoPlansAsync(progress: null, ct).ConfigureAwait(false);
        PowerSchemeInfo? original = await cpuPower.GetActiveSchemeAsync(ct).ConfigureAwait(false);

        List<PowerBenchmarkResult> results = [];
        try
        {
            _ = await Task.Run(() => CpuPowerBenchmark.Run(TimeSpan.FromMilliseconds(400), ct), ct).ConfigureAwait(false);

            CoclicoPowerMode[] modes = [CoclicoPowerMode.Economy, CoclicoPowerMode.Optimal, CoclicoPowerMode.Performance];

            for (int i = 0; i < modes.Length; i++)
            {
                CoclicoPowerMode mode = modes[i];
                status?.Report($"Test du plan « {PowerPlanCatalog.GetPlanName(mode)} »...");
                progress?.Report(new PowerBenchmarkStep(
                    mode,
                    PowerPlanCatalog.GetPlanName(mode),
                    0,
                    (100.0 * i) / modes.Length));

                (bool activated, _) = await cpuPower.ActivatePlanAsync(mode, ct).ConfigureAwait(false);
                if (!activated)
                {
                    continue;
                }

                await Task.Delay(1500, ct).ConfigureAwait(false);
                double score = await Task.Run(() => CpuPowerBenchmark.Run(TimeSpan.FromSeconds(5), ct), ct).ConfigureAwait(false);
                results.Add(new PowerBenchmarkResult(mode, score));
                progress?.Report(new PowerBenchmarkStep(
                    mode,
                    PowerPlanCatalog.GetPlanName(mode),
                    score,
                    (100.0 * (i + 1)) / modes.Length));
            }
        }
        finally
        {
            if (original != null)
            {
                status?.Report("Restauration du plan de départ...");
                CoclicoPowerMode? originalMode = PowerPlanCatalog.TryGetModeFromPlanName(original.Name);
                _ = originalMode is { } mode
                    ? await cpuPower.ActivatePlanAsync(mode, CancellationToken.None).ConfigureAwait(false)
                    : await cpuPower.ActivateRawSchemeAsync(original.Guid, original.Name, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return results;
    }
}
