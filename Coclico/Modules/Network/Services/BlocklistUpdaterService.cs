using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coclico.Services;

internal static class BlocklistUpdaterService
{
    public const int MaxDomains = 250000;

    public static readonly string[] DefaultListUrls =
    [
        "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/hosts/pro.txt",
        "https://big.oisd.nl/hosts",
        "https://raw.githubusercontent.com/badmojr/1Hosts/master/Pro/hosts.txt",
        "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts"
    ];

    private static readonly Regex DomainRegex = new(
        @"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coclico");

    public static string CachePath => Path.Combine(CacheDir, "blocklists-cache.json");

    public static string? ParseHostsLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        string s = line.Trim();
        if (s.StartsWith('#') || s.StartsWith('!') || s.StartsWith('[')) return null;
        int sp = s.IndexOfAny([' ', '\t']);
        if (sp > 0)
        {
            string candidate = s.Substring(sp + 1).Trim();
            if (candidate.StartsWith('*')) return null;
            s = candidate;
        }
        if (s.StartsWith("||", StringComparison.Ordinal) && s.EndsWith('^')) s = s.Substring(2, s.Length - 3);
        s = s.TrimEnd('.', '^', '/').ToLowerInvariant();
        if (s.Length < 4 || s.Contains('/') || s.Contains(' ')) return null;
        return DomainRegex.IsMatch(s) ? s : null;
    }

    public static HashSet<string> ParseList(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n'))
        {
            string? d = ParseHostsLine(line);
            if (d != null && set.Count < MaxDomains) set.Add(d);
        }
        return set;
    }
    public static async Task<HashSet<string>> FetchAndCacheAsync(IEnumerable<string>? urls = null, CancellationToken ct = default)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string url in urls ?? DefaultListUrls)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                string text = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
                foreach (string d in ParseList(text)) merged.Add(d);
            }
            catch
            {
            }
        }

        if (merged.Count > 0)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                var payload = new { fetchedUtc = DateTime.UtcNow, domains = merged.ToArray() };
                await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(payload), ct).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        return merged.Count > 0 ? merged : LoadCached();
    }

    public static HashSet<string> LoadCached()
    {
        try
        {
            if (!File.Exists(CachePath)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var payload = JsonSerializer.Deserialize<CachePayload>(File.ReadAllText(CachePath));
            return payload?.Domains != null
                ? new HashSet<string>(payload.Domains, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class CachePayload
    {
        public DateTime FetchedUtc { get; set; }
        public string[] Domains { get; set; } = [];
    }
}
