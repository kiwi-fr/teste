namespace Coclico.Modules.Diagnostics.Rules;

internal sealed record VolumeFacts(
    string VolumeName,
    long TotalBytes,
    long FreeBytes,
    string FileSystem,
    string DriveType);

internal sealed record DiskReliabilityFacts(
    string DiskName,
    long? TemperatureC,
    long? WearPercent,
    long? ReadErrorsUncorrected,
    long? WriteErrorsUncorrected,
    long? PowerOnHours,
    string? HealthStatus);

internal sealed record StorageFacts(
    IReadOnlyList<VolumeFacts> Volumes,
    IReadOnlyList<DiskReliabilityFacts> Disks,
    bool DisksEnumerated);

internal sealed record MemoryFacts(
    long TotalPhysicalBytes,
    long AvailablePhysicalBytes,
    long CommitTotalBytes,
    long CommitLimitBytes,
    int ProcessCount,
    string? TopProcessName,
    long? TopProcessWorkingSetBytes);

internal sealed record CpuFacts(
    string ProcessorName,
    int LogicalCoreCount,
    double LoadPercentMin,
    double LoadPercentMax,
    double? TemperatureC,
    bool TemperatureAvailable,
    string? TopProcessName,
    double? TopProcessCpuPercent);

internal sealed record GpuAdapterFacts(
    string Name,
    string? DriverVersion,
    long? AdapterRamBytes,
    string? Status);

internal sealed record GatewayProbeFacts(
    string? GatewayAddress,
    int ProbesSent,
    int ProbesReceived,
    double? MedianMs,
    double? P95Ms);

internal sealed record DnsProbeFacts(
    string HostName,
    bool Resolved,
    double? ResolveMs);

internal sealed record ExternalProbeFacts(
    string Destination,
    int ProbesSent,
    int ProbesReceived,
    double? MedianMs,
    double? P95Ms);

internal sealed record NetworkFacts(
    IReadOnlyList<string> UpAdapters,
    bool AnyAdapterUp,
    string? ActiveAdapterName,
    long? LinkSpeedMbps,
    GatewayProbeFacts Gateway,
    DnsProbeFacts Dns,
    IReadOnlyList<ExternalProbeFacts> ExternalProbes);

internal sealed record EventGroupFacts(
    string Provider,
    long EventId,
    int Count,
    DateTime? LastSeenUtc,
    string? Level);

internal sealed record WindowsFacts(
    string OsVersion,
    string OsBuild,
    TimeSpan Uptime,
    int UnexpectedShutdowns7d,
    IReadOnlyList<EventGroupFacts> CriticalGroups7d,
    bool EventLogAccessible);

internal sealed record DefenderFacts(
    bool StatusReadable,
    bool RealTimeProtectionEnabled,
    bool AntivirusEnabled,
    bool AntispywareEnabled,
    string? SignatureAgeDays);

internal sealed record DeviceProblemFacts(
    string DeviceName,
    string? DeviceClass,
    int ConfigManagerErrorCode);

internal sealed record BatteryFacts(
    bool BatteryPresent,
    bool OnAcPower,
    int? ChargePercent,
    int? EstimatedRuntimeMinutes,
    double? FullLifeRatio,
    bool DataAvailable);

internal sealed record ThermalFacts(
    double? ThermalZoneC,
    bool ThermalZoneAvailable);

internal sealed record ProcessFacts(
    int ProcessCount,
    string? TopCpuProcessName,
    double? TopCpuPercent,
    string? TopRamProcessName,
    double? TopRamPercent,
    bool StartupHealthReadable,
    bool StartupHealthy,
    string? StartupHealthMessage);
