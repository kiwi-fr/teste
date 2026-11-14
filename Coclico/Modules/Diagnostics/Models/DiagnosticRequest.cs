namespace Coclico.Modules.Diagnostics.Models;

internal sealed record DiagnosticRequest
{
    public required DiagnosticMode Mode { get; init; }

    public IReadOnlySet<DiagnosticCategory> Categories { get; init; } =
        new HashSet<DiagnosticCategory>();

    public TimeSpan PerCheckTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public int MaxParallelChecks { get; init; } = 3;

    public static DiagnosticRequest Quick() => new() { Mode = DiagnosticMode.Quick };

    public static DiagnosticRequest Full() => new() { Mode = DiagnosticMode.Full };

    public static DiagnosticRequest Targeted(IEnumerable<DiagnosticCategory> categories)
        => new() { Mode = DiagnosticMode.Targeted, Categories = categories.ToHashSet() };
}

internal sealed record DiagnosticProgress(
    string CurrentCheckId,
    DiagnosticCategory CurrentCategory,
    int CompletedChecks,
    int TotalChecks);

internal sealed record DiagnosticEnvironment(
    bool IsElevated,
    bool HasBattery,
    bool HardwareSensorsEnabled,
    string OsVersion);

internal sealed class DiagnosticContext
{
    public required DiagnosticEnvironment Environment { get; init; }

    public required DiagnosticRequest Request { get; init; }

    public required CancellationToken CancellationToken { get; init; }
}
