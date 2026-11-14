namespace Coclico.Modules.Diagnostics.Services.Hardware;

using Coclico.Modules.Diagnostics.Models;

internal sealed record HardwareSensorValue(
    string SensorId,
    string DisplayNameKey,
    double? Value,
    string Unit,
    DiagnosticValueState State);

internal sealed record HardwareSensorSnapshot(
    DateTime ReadAtUtc,
    IReadOnlyList<HardwareSensorValue> Values,
    bool AnySourceAvailable);

internal interface IHardwareTelemetryService : IAsyncDisposable
{
    Task<HardwareSensorSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
}
