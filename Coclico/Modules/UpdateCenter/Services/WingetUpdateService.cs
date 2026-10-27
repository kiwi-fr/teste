using System.Text.RegularExpressions;
using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.Services;

public partial class WingetUpdateService(SecureProcessRunner runner)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    public virtual async Task<IReadOnlyList<UpdateItem>> ScanAsync(CancellationToken cancellationToken)
    {
        runner.Timeout = DefaultTimeout;
        var (exitCode, output) = await runner.RunAsync("winget.exe",
            new[] { "upgrade", "--include-unknown", "--accept-source-agreements", "--disable-interactivity" },
            cancellationToken).ConfigureAwait(false);

        if (exitCode is not (0 or -1978335189))
            throw new InvalidOperationException($"winget exit code {exitCode}");

        return Parse(output);
    }

    public static IReadOnlyList<UpdateItem> Parse(string output)
    {
        var items = new List<UpdateItem>();

        foreach (var columns in output.Replace("\r", string.Empty)
            .Split('\n')
            .Select(rawLine => rawLine.Trim())
            .Where(raw => raw.Length > 0 && !raw.StartsWith('-'))
            .Select(line => ColumnSeparator().Split(line))
            .Where(cols => cols.Length >= 4 && !cols[0].Equals("Name", StringComparison.OrdinalIgnoreCase)))
        {
            var name = columns[0].Trim();
            var id = columns[1].Trim();
            var current = columns[2].Trim();
            var available = columns[3].Trim();

            if (id.Length == 0 || current.Length == 0 || available.Length == 0)
                continue;

            var unknown = current.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

            items.Add(new UpdateItem
            {
                Id = id,
                Name = name,
                Kind = UpdateKind.Application,
                Source = UpdateSource.Winget,
                Status = unknown ? UpdateItemStatus.UnknownVersion : UpdateItemStatus.Available,
                CurrentVersion = current,
                AvailableVersion = available
            });
        }

        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    [GeneratedRegex("\\s{2,}")]
    private static partial Regex ColumnSeparator();

    public virtual async Task<bool> UpgradeAsync(string packageId, CancellationToken cancellationToken)
    {
        if (!WingetPackageIdValidator.IsValid(packageId))
            throw new InvalidOperationException("Invalid package id.");

        runner.Timeout = TimeSpan.FromMinutes(15);
        var (exitCode, _) = await runner.RunAsync("winget.exe",
            new[] { "upgrade", "--id", packageId, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" },
            cancellationToken).ConfigureAwait(false);

        return exitCode == 0;
    }
}
