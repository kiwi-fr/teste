using Coclico.Models.Network;

namespace Coclico.Services.Network;

public interface INetworkOptimizerService
{
    INetworkExperimentEngine Engine { get; }
    INetworkDiscoveryService DiscoveryService { get; }
    INetworkSnapshotService SnapshotService { get; }

    Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken ct = default);
    Task<TcpGlobalSettings> GetTcpSettingsAsync(CancellationToken ct = default);
    Task<NicHardwareSettings> GetNicHardwareSettingsAsync(string adapterIdOrGuid, CancellationToken ct = default);
    Task<bool> ApplyCustomTcpSettingsAsync(TcpGlobalSettings settings, string? adapterId = null, CancellationToken ct = default);
    Task<bool> ApplyCustomNicSettingsAsync(string adapterIdOrGuid, NicHardwareSettings settings, CancellationToken ct = default);
    Task<NetworkBenchmarkReport> ApplyPresetAsync(NetworkOptimizationPreset preset, string? targetAdapterId = null, Action<string, double>? progress = null, CancellationToken ct = default);
    Task<IReadOnlyList<DnsBenchmarkResult>> RunDnsBenchmarkAsync(Action<string, double>? progress = null, CancellationToken ct = default);
    Task<bool> ApplyDnsAsync(string adapterIdOrName, string primaryDns, string secondaryDns, CancellationToken ct = default);
    Task<MtuDetectionResult> DetectOptimalMtuAsync(string targetHost = "1.1.1.1", Action<string, double>? progress = null, CancellationToken ct = default);
    Task<bool> ApplyMtuAsync(string adapterName, int mtu, CancellationToken ct = default);
    Task<NetworkBenchmarkReport> RunAutoTuningOptimizationAsync(string? targetAdapterId = null, Action<string, double>? progress = null, CancellationToken ct = default);
    Task<NetworkBenchmarkReport> RunComprehensiveCalibrationAsync(string? targetAdapterId, bool deepMode, Action<CalibrationStepProgress>? stepProgress = null, Action<string, double>? progress = null, CancellationToken ct = default);
    Task<string> RunAdaptiveEngineSessionAsync(string? targetAdapterId = null, OptimizationProfile profile = OptimizationProfile.Gaming, EngineExecutionMode mode = EngineExecutionMode.Auto, int samplesPerTest = 10, Action<EngineProgressInfo>? onProgress = null, Action<CognitiveLogEntry>? onLog = null, CancellationToken ct = default);
    Task<string> RunGlobalUnifiedOptimizationAsync(string? targetAdapterId = null, OptimizationProfile profile = OptimizationProfile.Gaming, EngineExecutionMode mode = EngineExecutionMode.Auto, Action<EngineProgressInfo>? onProgress = null, Action<CognitiveLogEntry>? onLog = null, CancellationToken ct = default);
    Task<bool> RestoreWindowsDefaultsAsync(string? targetAdapterId = null, Action<string, double>? progress = null, CancellationToken ct = default);
    Task<(double PingMs, double JitterMs, double PacketLoss)> MeasureLatencyAsync(string targetHost = "1.1.1.1", int count = 5, CancellationToken ct = default);
}

public interface INetworkExperimentEngine
{
    EngineState CurrentState { get; }
    EngineProgressInfo CurrentProgress { get; }
    event Action<EngineProgressInfo>? OnProgressUpdated;
    event Action<CognitiveLogEntry>? OnLogEntryAdded;

    Task<string> RunAdaptiveOptimizationSessionAsync(
        string? targetAdapterId = null,
        OptimizationProfile profile = OptimizationProfile.Gaming,
        EngineExecutionMode mode = EngineExecutionMode.Auto,
        int samplesPerTest = 10,
        CancellationToken ct = default);

    Task CancelSessionAsync();
    Task RequestEmergencyRollbackAsync();
}

public enum EngineState
{
    Idle,
    Running,
    Paused,
    RollingBack,
    Completed,
    Cancelled,
    Failed
}

public interface INetworkBenchmarkService
{
    Task<NetworkMetricReport> RunComprehensiveBenchmarkAsync(
        string? gatewayAddress = null,
        int pingSamples = 10,
        bool includeBufferbloat = true,
        bool includeDns = true,
        Action<string, double>? progress = null,
        CancellationToken ct = default);

    Task<NetworkMetricReport> RunFourPrecisionTestsAsync(
        string? gatewayAddress = null,
        int samplesPerTest = 10,
        Action<int, string, int, int, double>? onSampleProgress = null,
        CancellationToken ct = default);

    Task<(double MedianMs, double MeanMs, double JitterMs, double P95Ms, double PacketLossPercent)> RunStatisticalPingAsync(
        string host,
        int sampleCount = 10,
        int timeoutMs = 800,
        CancellationToken ct = default);

    Task<double> MeasureRealThroughputMbpsAsync(
        int samplesCount = 10,
        Action<int, int, double>? onSampleProgress = null,
        CancellationToken ct = default);

    Task<(double BufferbloatLatencyMs, string Grade)> MeasureBufferbloatAsync(
        string host = "1.1.1.1",
        CancellationToken ct = default);

    Task<(double ResolutionMs, double SuccessRatePercent)> BenchmarkRealDnsResolutionAsync(
        IReadOnlyList<string>? testDomains = null,
        CancellationToken ct = default);

    Task<MtuDetectionResult> DetectOptimalMtuAsync(
        string targetHost = "1.1.1.1",
        CancellationToken ct = default);
}

public interface INetworkDiscoveryService
{
    Task<IReadOnlyList<NetworkAdapterHardwareDetails>> DiscoverPhysicalAdaptersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DynamicNetworkParameter>> DiscoverAdapterParametersAsync(string adapterIdOrGuid, string? adapterName = null, CancellationToken ct = default);
    Task<bool> RefreshWifiTelemetryAsync(NetworkAdapterHardwareDetails adapter, CancellationToken ct = default);
}

public interface INetworkSafetyService
{
    void ClassifyParameter(DynamicNetworkParameter parameter);
    bool IsAllowedInMode(DynamicNetworkParameter parameter, EngineExecutionMode mode);
    bool ValidateProposedValue(DynamicNetworkParameter parameter, string proposedValue, out string failureReason);
    string GetExplanationForParameter(DynamicNetworkParameter parameter);
}

public interface INetworkScoringService
{
    double ComputeCompositeScore(NetworkMetricReport report, OptimizationProfile profile);
    (bool IsImprovement, ExperimentDecision Decision, string Reason) CompareMetrics(
        NetworkMetricReport baseline,
        NetworkMetricReport tested,
        OptimizationProfile profile);
}

public interface INetworkDiagnosticsService
{
    IReadOnlyList<string> DiagnoseBottlenecks(NetworkAdapterHardwareDetails adapter, NetworkMetricReport metrics);
    string GenerateStorytellingReport(
        NetworkAdapterHardwareDetails adapter,
        OptimizationProfile profile,
        NetworkMetricReport baseline,
        NetworkMetricReport finalMetrics,
        IReadOnlyList<ExperimentRecord> experiments);
}

public interface INetworkMemoryService
{
    HardwareKnowledgeProfile? GetProfile(string hardwareFingerprint);
    Task RecordExperimentAsync(string hardwareFingerprint, string adapterDesc, string driverVer, ExperimentRecord experiment, CancellationToken ct = default);
    Task SaveProvenBestSettingAsync(string hardwareFingerprint, string keyword, string value, double score, CancellationToken ct = default);
    Task MarkKeywordHarmfulAsync(string hardwareFingerprint, string keyword, CancellationToken ct = default);
}

public interface INetworkSnapshotService
{
    NetworkConfigurationSnapshot? InitialBaselineSnapshot { get; }
    Task<NetworkConfigurationSnapshot> CaptureSnapshotAsync(string adapterIdOrGuid, string adapterName, string label, CancellationToken ct = default);
    Task<bool> RollbackToSnapshotAsync(NetworkConfigurationSnapshot snapshot, CancellationToken ct = default);
    Task<bool> RollbackSingleParameterAsync(string adapterIdOrGuid, string keyword, string originalValue, CancellationToken ct = default);
    Task<bool> RollbackToBaselineAsync(CancellationToken ct = default);
    IReadOnlyList<NetworkConfigurationSnapshot> GetStoredSnapshots();
}

public interface INetworkWatchdogService : IDisposable
{
    bool IsMonitoring { get; }
    void StartMonitoring(string adapterId, string gatewayIp, Func<string, Task> onEmergencyRollback);
    void StopMonitoring();
    Task<bool> QuickHealthCheckAsync(string gatewayIp, CancellationToken ct = default);
}