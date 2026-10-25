using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.Services;

public class SystemUpdateService(SecureProcessRunner runner)
{
    public virtual async Task<UpdateItem> GetWindowsUpdateStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            runner.Timeout = TimeSpan.FromMinutes(2);
            var (exitCode, output) = await runner.RunAsync("usoclient.exe", new[] { "ScanInstallWait" }, cancellationToken).ConfigureAwait(false);
            return UpdateItem.UpToDate("windows", "Windows Update", UpdateKind.Windows, UpdateSource.WindowsUpdate);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new UpdateItem
            {
                Id = "windows",
                Name = "Windows Update",
                Kind = UpdateKind.Windows,
                Source = UpdateSource.WindowsUpdate,
                Status = UpdateItemStatus.UnknownVersion
            };
        }
    }
}
