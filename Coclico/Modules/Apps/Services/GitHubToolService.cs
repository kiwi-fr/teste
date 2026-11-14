using System.Collections.Frozen;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coclico.Security.Infrastructure;
using Coclico.Services;

namespace Coclico.Modules.Apps.Services;

internal sealed class GitHubToolService
{
    internal sealed record GithubAsset(string Name, string DownloadUrl);

    internal sealed record GithubToolResult(bool Success, string ExtractedDir, string? ExePath, string? Error, bool NeedsExplicitConfirmation = false);

    private static readonly HttpClient Http = new(new OutboundDomainGuardHandler())
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    private static readonly FrozenSet<string> _approvedRepos = new[]
    {
        "hbashton/DS4Windows",
        "Orbmu2k/nvidiaProfileInspector",
        "GSDragoon/RadeonSoftwareSlimmer",
        "MicaForEveryone/MicaForEveryone",
        "sorayuki/obs-multi-rtmp",
        "iandiv/appgroup",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly IProcessExecutionService _processExecution;

    public GitHubToolService(IProcessExecutionService processExecution)
    {
        _processExecution = processExecution ?? throw new ArgumentNullException(nameof(processExecution));
    }

    public static string GetAppsRootDir()
    {
        return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Coclico Apps");
    }

    public static string GetToolDir(string toolName)
    {
        return Path.Join(GetAppsRootDir(), AppCatalogService.GetSafeFolderName(toolName));
    }

    public async Task<GithubAsset> GetLatestAssetAsync(string repo, string pattern, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Coclico", "2.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using HttpResponseMessage response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        var assets = new List<(string Name, string Url)>();
        if (doc.RootElement.TryGetProperty("assets", out JsonElement assetsElement))
        {
            foreach (JsonElement asset in assetsElement.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null;
                string? url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement) ? urlElement.GetString() : null;
                if (name != null && url != null)
                {
                    assets.Add((name, url));
                }
            }
        }

        (string Name, string Url) match = assets.FirstOrDefault(a => Regex.IsMatch(a.Name, pattern));
        if (string.IsNullOrEmpty(match.Name))
        {
            throw new InvalidOperationException($"Aucun fichier de release ne correspond à '{pattern}' pour {repo}.");
        }

        return new GithubAsset(match.Name, match.Url);
    }

    private async Task<string?> TryGetExpectedChecksumAsync(string repo, string assetName, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Coclico", "2.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using HttpResponseMessage response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("assets", out JsonElement assetsElement))
            {
                return null;
            }

            foreach (JsonElement asset in assetsElement.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() : null;
                string? url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement) ? urlElement.GetString() : null;
                if (name is null || url is null)
                {
                    continue;
                }

                bool isChecksumFile = name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase);
                if (!isChecksumFile)
                {
                    continue;
                }

                string content = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
                string? hash = content.Split('\n')
                    .Select(rawLine => rawLine.Trim().TrimStart('*', '-'))
                    .Where(raw => raw.Length > 0)
                    .Select(line => line.Split(' ', 2))
                    .Select(parts => (Hash: parts[0].Trim(), FilePart: parts.Length > 1 ? parts[1].Trim() : null))
                    .Where(entry => entry.Hash.Length == 64)
                    .Where(entry => entry.FilePart is null || entry.FilePart.EndsWith(assetName, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.Hash)
                    .FirstOrDefault();

                if (hash is not null)
                {
                    return hash;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            LoggingService.LogException(ex, $"GitHubToolService.TryGetExpectedChecksumAsync({repo})");
            return null;
        }
    }

    public async Task<GithubToolResult> InstallToolAsync(string toolName, string repo, string pattern, IProgress<string>? log = null, CancellationToken ct = default, bool allowUnverified = false)
    {
        if (!_approvedRepos.Contains(repo))
        {
            LoggingService.LogWarning($"[GitHubToolService] Dépôt GitHub non approuvé refusé : {repo}");
            return new GithubToolResult(false, string.Empty, null, $"Dépôt GitHub non approuvé : {repo}.");
        }

        try
        {
            string root = GetAppsRootDir();
            Directory.CreateDirectory(root);

            log?.Report($"GitHub : recherche de la dernière release de {toolName} ({repo})...");
            GithubAsset asset = await GetLatestAssetAsync(repo, pattern, ct).ConfigureAwait(false);

            string destination = Path.Join(root, asset.Name);
            string partial = destination + ".part";
            try
            {
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }

            log?.Report($"Téléchargement : {asset.Name}...");
            using (HttpResponseMessage response = await Http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                FileStream fs = File.Create(partial);
                await using (fs.ConfigureAwait(false))
                {
                    await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
                }
            }

            if (!File.Exists(partial) || new FileInfo(partial).Length < 1)
            {
                return new GithubToolResult(false, string.Empty, null, "Fichier téléchargé vide ou absent.");
            }

            string? expectedHash = await TryGetExpectedChecksumAsync(repo, asset.Name, ct).ConfigureAwait(false);
            if (expectedHash is null && !allowUnverified)
            {
                LoggingService.LogWarning($"[GitHubToolService] Installation refusée : aucune somme de contrôle publiée pour {asset.Name} ({repo}).");
                return new GithubToolResult(false, string.Empty, null,
                    $"Aucune somme de contrôle SHA-256 publiée pour {asset.Name}. Une confirmation explicite est requise pour installer un téléchargement non vérifiable.",
                    NeedsExplicitConfirmation: true);
            }

            if (expectedHash is not null)
            {
                string actualHash;
                FileStream hashStream = File.OpenRead(partial);
                await using (hashStream.ConfigureAwait(false))
                {
                    actualHash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, ct).ConfigureAwait(false));
                }

                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    return new GithubToolResult(false, string.Empty, null,
                        $"Somme de contrôle SHA-256 invalide pour {asset.Name} (attendu {expectedHash}, obtenu {actualHash}).");
                }

                log?.Report($"SHA-256 vérifié : {asset.Name}");
            }
            else
            {
                log?.Report($"Aucune somme de contrôle publiée pour {asset.Name} : installation en mode explicitement confirmé.");
                LoggingService.LogWarning($"[GitHubToolService] Release sans checksum pour {asset.Name} ({repo}) : installation confirmée par l'utilisateur.");
            }

            File.Move(partial, destination, true);
            MarkOfTheWeb.Apply(destination);
            log?.Report($"Téléchargement terminé : {Math.Round(new FileInfo(destination).Length / 1024.0 / 1024.0, 2)} Mo");

            string toolDir = GetToolDir(toolName);
            string? exePath = null;

            if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(toolDir);

                using ZipArchive archive = ZipFile.OpenRead(destination);
                if (!ArchiveGuard.IsEntryCountAllowed(archive.Entries.Count))
                {
                    return new GithubToolResult(false, string.Empty, null, "Archive refusée : nombre d'entrées anormal.");
                }

                long accumulatedBytes = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!ArchiveGuard.IsSafeArchiveEntry(entry.FullName))
                    {
                        LoggingService.LogWarning($"[GitHubToolService] Entrée d'archive non sûre refusée : {entry.FullName}");
                        return new GithubToolResult(false, string.Empty, null, $"Archive refusée : entrée non sûre '{entry.FullName}'.");
                    }

                    if (!ArchiveGuard.IsEntrySizeAllowed(entry.Length, accumulatedBytes))
                    {
                        return new GithubToolResult(false, string.Empty, null, "Archive refusée : taille totale excessive.");
                    }

                    accumulatedBytes += entry.Length;
                }

                archive.ExtractToDirectory(toolDir, overwriteFiles: true);

                log?.Report($"Extrait dans {toolDir}");
                exePath = FindMainExecutable(toolDir, toolName);
            }
            else if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                exePath = destination;
                log?.Report($"EXE téléchargé (non exécuté automatiquement) : {destination}");
            }

            return new GithubToolResult(true, toolDir, exePath, null);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException)
        {
            LoggingService.LogException(ex, $"GitHubToolService.InstallToolAsync({toolName})");
            return new GithubToolResult(false, string.Empty, null, ex.Message);
        }
    }

    private static string? FindMainExecutable(string dir, string toolName)
    {
        string[] cleanWords = toolName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(static w => w.Length > 2)
            .ToArray();

        var executables = Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories)
            .Where(static f => !Path.GetFileName(f).Contains("uninst", StringComparison.OrdinalIgnoreCase))
            .ToList();

        string? best = executables.FirstOrDefault(f =>
        {
            string fileName = Path.GetFileNameWithoutExtension(f);
            return cleanWords.All(w => fileName.Contains(w, StringComparison.OrdinalIgnoreCase));
        });

        best ??= executables.FirstOrDefault(f =>
        {
            string fileName = Path.GetFileNameWithoutExtension(f);
            return cleanWords.Any(w => fileName.Contains(w, StringComparison.OrdinalIgnoreCase));
        });

        best ??= executables.OrderBy(static f => f.Length).FirstOrDefault();
        return best;
    }

    public async Task CreateShortcutForExeAsync(string shortcutName, string exePath, CancellationToken ct = default)
    {
        string shortcutDir = AppShortcutsService.GetShortcutDir();
        Directory.CreateDirectory(shortcutDir);
        string linkPath = Path.Join(shortcutDir, AppCatalogService.GetSafeFolderName(shortcutName) + ".lnk");

        string script =
            "$s = (New-Object -ComObject WScript.Shell).CreateShortcut('" + linkPath.Replace("'", "''") + "'); " +
            "$s.TargetPath = '" + exePath.Replace("'", "''") + "'; " +
            "$s.WorkingDirectory = '" + Path.GetDirectoryName(exePath)?.Replace("'", "''") + "'; " +
            "$s.Save()";

        _ = await _processExecution.ExecuteAsync(
            "powershell.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
            timeout: TimeSpan.FromSeconds(30),
            ct: ct).ConfigureAwait(false);
    }
}
