using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.Services;

internal class UpdateScanService(
    WingetUpdateService winget,
    DriverUpdateService drivers,
    SystemUpdateService system)
{
    public virtual async Task<UpdateScanResult> ScanAllAsync(CancellationToken cancellationToken)
    {
        Task<ScanChunk> wingetTask = RunList(() => winget.ScanAsync(cancellationToken));
        Task<ScanChunk> driversTask = RunList(() => drivers.SearchWindowsUpdateDriverUpdatesAsync(cancellationToken));
        Task<ScanChunk> systemTask = RunSingle(() => system.GetWindowsUpdateStatusAsync(cancellationToken));

        await Task.WhenAll(wingetTask, driversTask, systemTask).ConfigureAwait(false);

        var items = new List<UpdateItem>();
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        Append(items, errors, UpdateKind.Application, wingetTask.Result);
        Append(items, errors, UpdateKind.Driver, driversTask.Result);
        Append(items, errors, UpdateKind.Windows, systemTask.Result);

        return new UpdateScanResult
        {
            Items = items.OrderBy(i => i.Kind).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            ScannedAt = DateTimeOffset.UtcNow,
            SourceErrors = errors
        };
    }

    private static void Append(List<UpdateItem> items, Dictionary<string, string> errors, UpdateKind kind, ScanChunk chunk)
    {
        items.AddRange(chunk.Items);
        if (chunk.Error is not null)
        {
            errors[kind.ToString()] = chunk.Error;
        }
    }

    private static async Task<ScanChunk> RunList(Func<Task<IReadOnlyList<UpdateItem>>> factory)
    {
        try
        {
            IReadOnlyList<UpdateItem>? scanned = await factory().ConfigureAwait(false);
            return new ScanChunk(scanned is null ? [] : [.. scanned], null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ScanChunk([], ex.Message);
        }
    }

    private static async Task<ScanChunk> RunSingle(Func<Task<UpdateItem>> factory)
    {
        try
        {
            UpdateItem? item = await factory().ConfigureAwait(false);
            return new ScanChunk(item is null ? [] : [item], null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ScanChunk([], ex.Message);
        }
    }

    private sealed record ScanChunk(List<UpdateItem> Items, string? Error);
}
