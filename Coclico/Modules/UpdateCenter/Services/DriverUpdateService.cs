using System.Management;
using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.Services;

internal class DriverUpdateService
{
    private const System.Reflection.BindingFlags Invoke = System.Reflection.BindingFlags.InvokeMethod;
    private const System.Reflection.BindingFlags Get = System.Reflection.BindingFlags.GetProperty;
    private const System.Reflection.BindingFlags Set = System.Reflection.BindingFlags.SetProperty;

    public virtual async Task<IReadOnlyList<UpdateItem>> SearchWindowsUpdateDriverUpdatesAsync(CancellationToken cancellationToken)
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (sessionType is null)
            return Array.Empty<UpdateItem>();

        var session = Activator.CreateInstance(sessionType);
        var searcher = sessionType.InvokeMember("CreateUpdateSearcher", Invoke, null, session, null, System.Globalization.CultureInfo.InvariantCulture);
        var searcherType = searcher!.GetType();

        var searchResult = await Task.Run(
            () => searcherType.InvokeMember("Search", Invoke, null, searcher, new object[] { "IsInstalled=0 and Type='Driver'" }, System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);

        var updatesCollection = (System.Collections.IEnumerable)searcherType.InvokeMember("Updates", Get, null, searchResult, null, System.Globalization.CultureInfo.InvariantCulture)!;
        var items = new List<UpdateItem>();

        foreach (var update in updatesCollection)
        {
            var updateType = update.GetType();
            var title = updateType.InvokeMember("Title", Get, null, update, null, System.Globalization.CultureInfo.InvariantCulture) as string ?? "Pilote";

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

    public virtual async Task<bool> InstallAsync(UpdateItem item, CancellationToken cancellationToken)
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (sessionType is null || string.IsNullOrWhiteSpace(item.Name))
            return false;

        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var session = Activator.CreateInstance(sessionType)!;
            var searcher = sessionType.InvokeMember("CreateUpdateSearcher", Invoke, null, session, null, System.Globalization.CultureInfo.InvariantCulture)!;
            var searchResult = searcher.GetType().InvokeMember("Search", Invoke, null, searcher, new object[] { "IsInstalled=0 and Type='Driver'" }, System.Globalization.CultureInfo.InvariantCulture)!;
            var updates = (System.Collections.IEnumerable)searchResult.GetType().InvokeMember("Updates", Get, null, searchResult, null, System.Globalization.CultureInfo.InvariantCulture)!;

            object? target = null;
            foreach (var update in updates)
            {
                var title = update.GetType().InvokeMember("Title", Get, null, update, null, System.Globalization.CultureInfo.InvariantCulture) as string;
                if (string.Equals(title, item.Name, StringComparison.OrdinalIgnoreCase))
                {
                    target = update;
                    break;
                }
            }

            if (target is null)
                return false;

            var collectionType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl");
            if (collectionType is null)
                return false;

            var collection = Activator.CreateInstance(collectionType);
            _ = collection!.GetType().InvokeMember("Add", Invoke, null, collection, new[] { target }, System.Globalization.CultureInfo.InvariantCulture);

            cancellationToken.ThrowIfCancellationRequested();

            var downloader = sessionType.InvokeMember("CreateUpdateDownloader", Invoke, null, session, null)!;
            downloader.GetType().InvokeMember("Updates", Set, null, downloader, new[] { collection });
            var downloadResult = downloader.GetType().InvokeMember("Download", Invoke, null, downloader, null);
            var downloadCode = (int)(downloadResult!.GetType().InvokeMember("ResultCode", Get, null, downloadResult, null) ?? 1);
            if (downloadCode != 2)
                return false;

            cancellationToken.ThrowIfCancellationRequested();

            var installer = sessionType.InvokeMember("CreateUpdateInstaller", Invoke, null, session, null, System.Globalization.CultureInfo.InvariantCulture)!;
            installer.GetType().InvokeMember("Updates", Set, null, installer, new[] { collection }, System.Globalization.CultureInfo.InvariantCulture);
            var installResult = installer.GetType().InvokeMember("Install", Invoke, null, installer, null);
            var installCode = (int)(installResult!.GetType().InvokeMember("ResultCode", Get, null, installResult, null) ?? 1);
            return installCode == 2;
        }, cancellationToken).ConfigureAwait(false);
    }
}
