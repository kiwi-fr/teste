using System.Management;
using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.Services;

public class DriverUpdateService
{
    public virtual async Task<IReadOnlyList<DriverInfo>> GetInventoryAsync(CancellationToken cancellationToken)
    {
        var results = new List<DriverInfo>();

        using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, Manufacturer, DriverVersion, DriverDate FROM Win32_PnPSignedDriver");
        foreach (var managementObject in searcher.Get())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var deviceId = managementObject["DeviceID"] as string;
            var name = managementObject["Name"] as string;

            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(name))
                continue;

            DateTime? driverDate = null;
            if (managementObject["DriverDate"] is string dateString)
                driverDate = ManagementDateTimeConverter.ToDateTime(dateString);

            results.Add(new DriverInfo
            {
                DeviceId = deviceId,
                DeviceName = name,
                Manufacturer = managementObject["Manufacturer"] as string,
                DriverVersion = managementObject["DriverVersion"] as string,
                DriverDate = driverDate,
                IsUpdatable = true
            });
        }

        return await Task.FromResult(results).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<UpdateItem>> SearchWindowsUpdateDriverUpdatesAsync(CancellationToken cancellationToken)
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (sessionType is null)
            return Array.Empty<UpdateItem>();

        var session = Activator.CreateInstance(sessionType);
        var searcher = sessionType.InvokeMember("CreateUpdateSearcher", System.Reflection.BindingFlags.InvokeMethod, null, session, null);
        var searcherType = searcher!.GetType();

        var searchResult = await Task.Run(
            () => searcherType.InvokeMember("Search", System.Reflection.BindingFlags.InvokeMethod, null, searcher, new object[] { "IsInstalled=0 and Type='Driver'" }),
            cancellationToken).ConfigureAwait(false);

        var updatesCollection = (System.Collections.IEnumerable)searcherType.InvokeMember("Updates", System.Reflection.BindingFlags.GetProperty, null, searchResult, null)!;
        var items = new List<UpdateItem>();

        foreach (var update in updatesCollection)
        {
            var updateType = update.GetType();
            var title = updateType.InvokeMember("Title", System.Reflection.BindingFlags.GetProperty, null, update, null) as string ?? "Pilote";

            items.Add(new UpdateItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = title,
                Kind = UpdateKind.Driver,
                Source = UpdateSource.WindowsUpdate,
                Status = UpdateItemStatus.Available
            });
        }

        return items;
    }
}