using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Coclico.Services;

internal class UpdateManager(ILogger<UpdateManager> logger)
{
    private const string GitHubApiUrl = "https://api.github.com/repos";
    private const string GitHubOwner = "Coclico-cy";
    private const string GitHubRepo = "Coclico";
    private const long MaxInstallerSizeBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxHashFileBytes = 64 * 1024;

    private static readonly HttpClient _httpClient = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, SignedIntegrityManifest> _authenticatedManifests = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions ReleaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };
    private static readonly Regex NamedHashLineRegex = new(@"^([a-fA-F0-9]{64})\s+\*?(\S.*)$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    private static readonly Regex BareHashLineRegex = new(@"^([a-fA-F0-9]{64})$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    private static readonly Regex SetupAssetPattern =
        new(@"^Coclico-\d+(\.\d+){0,3}\.exe$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

    private readonly ConcurrentDictionary<string, string> _verifiedAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<UpdateManager> _logger = logger;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new Coclico.Security.Infrastructure.OutboundDomainGuardHandler());
        client.DefaultRequestHeaders.Add(
            "User-Agent",
            "Coclico-UpdateManager/1.0.0 (+https://github.com/" + GitHubOwner + "/" + GitHubRepo + ")"
        );
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    private static async Task<HttpResponseMessage> GetAsyncWithTimeoutAsync(
        string url, TimeSpan timeout, HttpCompletionOption completion, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await _httpClient.GetAsync(url, completion, cts.Token).ConfigureAwait(false);
    }

    public async Task<GitHubRelease?> CheckForUpdatesAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation($"Checking for updates... (current: {currentVersion})");

            string url = $"{GitHubApiUrl}/{GitHubOwner}/{GitHubRepo}/releases/latest";
            using HttpResponseMessage response = await GetAsyncWithTimeoutAsync(
                url, TimeSpan.FromSeconds(30), HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"GitHub API returned {response.StatusCode}");
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false) ?? string.Empty;
            GitHubRelease? release = JsonSerializer.Deserialize<GitHubRelease>(json, ReleaseJsonOptions);

            if (release == null)
            {
                _logger.LogWarning("Failed to parse GitHub release JSON");
                return null;
            }

            if (IsNewerVersion(release.TagName, currentVersion))
            {
                if (!await AuthenticateAndUpdatePolicyAsync(release, ct).ConfigureAwait(false))
                {
                    return null;
                }

                _logger.LogInformation($"Update available: {release.TagName} (current: {currentVersion})");
                return release;
            }

            _logger.LogInformation("Already up-to-date");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"HTTP error checking updates: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError($"JSON parsing error: {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Update check cancelled");
            return null;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException or System.Text.Json.JsonException or System.FormatException)
        {
            _logger.LogError($"Unexpected error checking updates: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> AuthenticateAndUpdatePolicyAsync(GitHubRelease release, CancellationToken ct)
    {
        TrustPolicyDocument document = LoadTrustPolicy();
        TrustPolicy trustPolicy = new() { Keys = document.Keys };

        ReleaseAuthenticationResult authentication = await ReleaseAuthentication
            .AuthenticateAsync(_httpClient, release, trustPolicy, ct)
            .ConfigureAwait(false);

        if (authentication.IsTrustedKeyConfigured && !authentication.IsAuthenticated)
        {
            _logger.LogError($"Refusing release {release.TagName}: {authentication.Reason}");
            return false;
        }

        if (authentication.IsAuthenticated && authentication.VerifiedManifest is not null)
        {
            _authenticatedManifests[release.TagName] = authentication.VerifiedManifest;
        }

        if (!authentication.IsAuthenticated)
        {
            _logger.LogWarning($"Release {release.TagName} is not signed: {authentication.Reason}");
        }

        UpdatePolicyResult policyResult = UpdatePolicyChecker.Evaluate(
            release.TagName,
            authentication.SecurityGeneration,
            document.UpdatePolicy);
        if (policyResult.Decision == UpdatePolicyDecision.Blocked)
        {
            _logger.LogError($"Refusing release {release.TagName}: {policyResult.Reason}");
            return false;
        }

        if (policyResult.Decision == UpdatePolicyDecision.RecoveryAllowed)
        {
            _logger.LogWarning($"Release {release.TagName} accepted as recovery: {policyResult.Reason}");
        }

        return true;
    }

    private static TrustPolicyDocument LoadTrustPolicy()
    {
        try
        {
            System.Reflection.Assembly assembly = typeof(UpdateManager).Assembly;
            using System.IO.Stream? stream = assembly.GetManifestResourceStream("Coclico.Security.trust-policy.json");
            if (stream != null)
            {
                return TrustStore.Load(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LoggingService.LogException(ex, "UpdateManager.LoadTrustPolicy");
        }

        return TrustStore.Empty();
    }

    public async Task<bool> DownloadReleaseAsync(
        GitHubRelease release, string assetName, string savePath,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        try
        {
            GitHubAsset? asset = release.Assets?.Find(a => string.Equals(a.Name, assetName, StringComparison.Ordinal));
            if (asset == null)
            {
                _logger.LogWarning($"Asset '{assetName}' not found in release {release.TagName}");
                return false;
            }

            if (!SetupAssetPattern.IsMatch(assetName))
            {
                _logger.LogError($"Refusing asset with unexpected name '{assetName}'.");
                return false;
            }

            string fullPath = Guard.EnsurePathInside(Path.GetTempPath(), savePath, nameof(savePath));

            if (!IsTrustedDownloadUrl(asset.BrowserDownloadUrl, out Uri? downloadUri))
            {
                _logger.LogError("Refusing update asset from an untrusted download URL.");
                return false;
            }

            if (asset.Size is <= 0 or > MaxInstallerSizeBytes)
            {
                _logger.LogError("Refusing update asset without a valid size.");
                return false;
            }

            if (LoadTrustPolicy().Keys.Count > 0 && !_authenticatedManifests.ContainsKey(release.TagName))
            {
                _logger.LogError("Refusing update asset from a release without an authenticated signed manifest.");
                return false;
            }

            string? expectedHash = await ResolveExpectedHashAsync(release, asset, assetName, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(expectedHash) || !IsSha256Hex(expectedHash))
            {
                _logger.LogError("Refusing update asset without a valid SHA-256 digest.");
                return false;
            }

            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            downloadCts.CancelAfter(TimeSpan.FromHours(2));
            using HttpResponseMessage assetResponse = await _httpClient
                .GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token)
                .ConfigureAwait(false);
            if (!assetResponse.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to download asset: {assetResponse.StatusCode}");
                return false;
            }

            if (assetResponse.Content.Headers.ContentLength is > MaxInstallerSizeBytes)
            {
                _logger.LogError("Refusing update asset: content length exceeds the size limit.");
                return false;
            }

            _ = Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
            string tempPath = fullPath + ".download-" + Guid.NewGuid().ToString("N");
            long totalBytesRead = 0;
            try
            {
                Stream contentStream = await assetResponse.Content.ReadAsStreamAsync(downloadCts.Token).ConfigureAwait(false);
                await using (contentStream.ConfigureAwait(false))
                {
                    var fileStream = new FileStream(
                    tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                    await using (fileStream.ConfigureAwait(false))
                    {
                        byte[] buffer = new byte[128 * 1024];
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), downloadCts.Token).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), downloadCts.Token).ConfigureAwait(false);
                            totalBytesRead += bytesRead;
                            if (totalBytesRead > MaxInstallerSizeBytes)
                            {
                                _logger.LogError("Refusing update asset: stream exceeded the size limit.");
                                return false;
                            }

                            progress?.Report(totalBytesRead);
                        }
                    }
                }

                if (asset.Size > 0 && totalBytesRead != asset.Size)
                {
                    _logger.LogWarning($"Incomplete download for '{assetName}': {totalBytesRead} of {asset.Size} bytes.");
                    return false;
                }

                TrustPolicyDocument embeddedPolicy = LoadTrustPolicy();
                if (embeddedPolicy.Keys.Count > 0 && !_authenticatedManifests.ContainsKey(release.TagName))
                {
                    ReleaseAuthenticationResult embedded = await ReleaseAuthentication
                        .AuthenticateEmbeddedAsync(tempPath, new TrustPolicy { Keys = embeddedPolicy.Keys }, release.TagName, ct)
                        .ConfigureAwait(false);
                    if (embedded.VerifiedManifest != null)
                    {
                        _authenticatedManifests[release.TagName] = embedded.VerifiedManifest;
                    }

                    if (!embedded.IsAuthenticated)
                    {
                        _logger.LogError($"Refusing update asset: embedded integrity manifest not authenticated ({embedded.Reason}).");
                        return false;
                    }
                }

                string actualHash;
                FileStream hashStream = File.OpenRead(tempPath);
                await using (hashStream.ConfigureAwait(false))
                {
                    actualHash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, downloadCts.Token).ConfigureAwait(false));
                }

                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
                {
                    _logger.LogError($"Downloaded update failed SHA-256 verification for '{assetName}' (expected {expectedHash}, actual {actualHash}).");
                    return false;
                }

                if (!await TryReplaceInstallerAsync(tempPath, fullPath).ConfigureAwait(false))
                {
                    _logger.LogError($"Cannot replace '{Path.GetFileName(fullPath)}': the file is locked by another process. Close the running application and retry the update.");
                    return false;
                }

                _verifiedAssets[fullPath] = expectedHash;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception exCleanup) when (exCleanup is System.IO.IOException or System.UnauthorizedAccessException)
                {
                    LoggingService.LogException(exCleanup, "UpdateManager.TempCleanup");
                }
            }

            _logger.LogInformation($"Download complete: {savePath}");
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException or System.Security.Cryptography.CryptographicException or System.OperationCanceledException)
        {
            _logger.LogError($"Error downloading release: {ex.Message}");
            return false;
        }
    }

    public bool LaunchInstaller(string filePath, string? expectedVersion = null)
    {
        try
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath) || !_verifiedAssets.TryGetValue(fullPath, out string? expectedHash))
            {
                _logger.LogError("Refusing to launch an installer that was not verified by this update manager.");
                return false;
            }

            using FileStream stream = File.OpenRead(fullPath);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
            {
                _logger.LogError("Refusing to launch an installer whose contents changed after verification.");
                return false;
            }

            string extension = Path.GetExtension(fullPath);
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError($"Refusing to launch an installer with unsupported extension '{extension}'.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedVersion) &&
                !InstallerVersionMatches(ReadInstallerVersion(fullPath), expectedVersion))
            {
                _logger.LogError($"Refusing to launch an installer whose file version does not match {expectedVersion}.");
                return false;
            }

            if (!IsAuthenticodeValid(fullPath))
            {
                if (EnforceAuthenticodeSignature)
                {
                    _logger.LogError("Refusing to launch an unsigned or invalidly signed installer.");
                    return false;
                }

                _logger.LogWarning("Installer is not Authenticode-signed; proceeding (SHA-256 integrity verified only).");
            }

            if (!IsSignerPinned(fullPath))
            {
                _logger.LogError("Refusing to launch an installer signed by an unexpected publisher.");
                return false;
            }

            _logger.LogInformation($"Launching verified installer: {fullPath}");
            var psi = new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetTempPath()
            };
            _ = Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.LogInformation("Installer launch cancelled by the user at the UAC prompt.");
            return false;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            _logger.LogError($"Failed to launch installer: {ex.Message}");
            return false;
        }
    }

    internal static bool IsNewerVersion(string latestTag, string currentVersion)
    {
        try
        {
            string latest = latestTag.TrimStart('v', 'V');
            string current = currentVersion.TrimStart('v', 'V');
            (string latestBase, string latestSuffix) = SplitVersionAndSuffix(latest);
            (string currentBase, string currentSuffix) = SplitVersionAndSuffix(current);
            List<int> latestParts = ParseVersionParts(latestBase);
            List<int> currentParts = ParseVersionParts(currentBase);
            for (int i = 0; i < 4; i++)
            {
                int latestPart = i < latestParts.Count ? latestParts[i] : 0;
                int currentPart = i < currentParts.Count ? currentParts[i] : 0;

                if (latestPart > currentPart)
                {
                    return true;
                }

                if (latestPart < currentPart)
                {
                    return false;
                }
            }

            bool latestIsPrerelease = !string.IsNullOrEmpty(latestSuffix);
            bool currentIsPrerelease = !string.IsNullOrEmpty(currentSuffix);
            return !latestIsPrerelease && currentIsPrerelease;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "UpdateManager.IsNewerVersion");
            return string.Compare(latestTag, currentVersion, StringComparison.Ordinal) > 0;
        }
    }

    internal static bool IsSkipped(string? tagName, string? skippedVersion)
    {
        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(skippedVersion))
        {
            return false;
        }

        static string Normalize(string value) => value.Trim().TrimStart('v', 'V');

        return string.Equals(Normalize(tagName), Normalize(skippedVersion), StringComparison.OrdinalIgnoreCase);
    }

    public static GitHubAsset? PickSetupAsset(GitHubRelease release)
    {
        return release.Assets?.FirstOrDefault(a => SetupAssetPattern.IsMatch(a.Name));
    }

    public static string? ReadInstallerVersion(string filePath)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(filePath);
            string? version = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, $"UpdateManager.ReadInstallerVersion({filePath})");
            return null;
        }
    }

    internal static bool InstallerVersionMatches(string? fileVersion, string expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(fileVersion) || string.IsNullOrWhiteSpace(expectedVersion))
        {
            return false;
        }

        (string fileCore, string fileSuffix) = SplitNumericAndSuffix(fileVersion);
        (string expectedCore, string expectedSuffix) = SplitNumericAndSuffix(expectedVersion);

        return SameNumericVersion(fileCore, expectedCore) &&
            string.Equals(fileSuffix, expectedSuffix, StringComparison.OrdinalIgnoreCase);

        static (string core, string suffix) SplitNumericAndSuffix(string value)
        {
            string version = value.Trim().TrimStart('v', 'V');
            int buildIndex = version.IndexOf('+');
            if (buildIndex >= 0)
            {
                version = version[..buildIndex];
            }

            int cut = version.IndexOfAny(['-', ' ']);
            return cut > 0
                ? (version[..cut], version[(cut + 1)..])
                : (version, string.Empty);
        }

        static bool SameNumericVersion(string left, string right)
        {
            List<int> a = ParseVersionParts(left);
            List<int> b = ParseVersionParts(right);
            int length = Math.Max(a.Count, b.Count);
            for (int i = 0; i < length; i++)
            {
                if ((i < a.Count ? a[i] : 0) != (i < b.Count ? b[i] : 0))
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal static string? ParseHashForAsset(string shaContent, string assetName)
    {
        var hashByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int unnamedHashCount = 0;
        string? lastUnnamedHash = null;

        foreach (string line in shaContent.Split('\n').Select(rawLine => rawLine.Trim()).Where(line => line.Length > 0))
        {

            Match m = NamedHashLineRegex.Match(line);
            if (m.Success)
            {
                string filename = m.Groups[2].Value.Trim().TrimStart('*');
                if (filename.StartsWith("./", StringComparison.Ordinal))
                {
                    filename = filename[2..];
                }

                hashByFile[filename] = m.Groups[1].Value;
                continue;
            }

            Match bare = BareHashLineRegex.Match(line);
            if (bare.Success)
            {
                unnamedHashCount++;
                lastUnnamedHash = bare.Groups[1].Value;
            }
        }

        if (hashByFile.TryGetValue(assetName, out string? namedHash))
        {
            return namedHash;
        }

        return unnamedHashCount == 1 ? lastUnnamedHash : null;
    }

    private static bool IsTrustedDownloadUrl(string? url, out Uri? uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            (!string.Equals(uri.Host, "github.com", StringComparison.Ordinal) && !string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.Ordinal)))
        {
            return false;
        }

        return true;
    }

    private static async Task<bool> TryReplaceInstallerAsync(string tempPath, string fullPath)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Move(tempPath, fullPath, overwrite: true);
                return true;
            }
            catch (Exception exMove) when (exMove is IOException or UnauthorizedAccessException)
            {
                LoggingService.LogException(exMove, "UpdateManager.TryReplaceInstaller.Move");
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(400).ConfigureAwait(false);
            }
        }

        return false;
    }

    private Task<string?> ResolveExpectedHashAsync(
        GitHubRelease release, GitHubAsset asset, string assetName, CancellationToken ct)
    {
        if (_authenticatedManifests.TryGetValue(release.TagName, out SignedIntegrityManifest? manifest))
        {
            ManifestFileEntry? entry = manifest.Manifest.Files.Find(f =>
                f.Path.Equals(assetName, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                return Task.FromResult<string?>(entry.Sha256);
            }

        }

        return ResolveExpectedHashFromChannelAsync(release, asset, assetName, ct);
    }

    private async Task<string?> ResolveExpectedHashFromChannelAsync(
        GitHubRelease release, GitHubAsset asset, string assetName, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(asset.Digest))
        {
            return asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? asset.Digest[7..]
                : asset.Digest;
        }

        GitHubAsset? shaAsset = release.Assets?.Find(a =>
            a.Name.Equals(assetName + ".sha256", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase));

        if (shaAsset == null || shaAsset.Size is <= 0 or > MaxHashFileBytes
            || !IsTrustedDownloadUrl(shaAsset.BrowserDownloadUrl, out Uri? shaUri))
        {
            return null;
        }

        try
        {
            using var shaCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            shaCts.CancelAfter(TimeSpan.FromSeconds(30));
            string shaContent = await _httpClient.GetStringAsync(shaUri, shaCts.Token).ConfigureAwait(false);

            return ParseHashForAsset(shaContent, assetName);
        }
        catch (Exception exShaAsset) when (exShaAsset is not System.OutOfMemoryException)
        {
            LoggingService.LogException(exShaAsset, "UpdateManager.ShaAssetDownload");
            return null;
        }
    }

    private static (string versionPart, string suffixPart) SplitVersionAndSuffix(string version)
    {
        int dashIndex = version.IndexOf('-');
        if (dashIndex > 0)
        {
            return (version[..dashIndex], version[(dashIndex + 1)..]);
        }

        int spaceIndex = version.IndexOf(' ');
        if (spaceIndex > 0)
        {
            return (version[..spaceIndex], version[(spaceIndex + 1)..]);
        }

        return (version, string.Empty);
    }

    private static List<int> ParseVersionParts(string versionString)
    {
        return versionString.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => int.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out _))
            .Select(int.Parse)
            .ToList();
    }

    private static bool IsSha256Hex(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        try
        {
            _ = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsAuthenticodeValid(string filePath)
    {
        try
        {
            WinTrustFileInfo info = new()
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePath
            };
            Guid action = WinTrustActionGenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref info) == 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LoggingService.LogException(ex, "UpdateManager.IsAuthenticodeValid");
            return false;
        }
    }

    private const bool EnforceAuthenticodeSignature = false;
    private const string? RequiredSignerSubjectDn = null;
    private const string? RequiredSignerThumbprint = null;

    private static bool IsSignerPinned(string filePath)
    {
        if (string.IsNullOrWhiteSpace(RequiredSignerSubjectDn) && string.IsNullOrWhiteSpace(RequiredSignerThumbprint))
        {
            TryLogSignerIdentity(filePath);
            return true;
        }

        try
        {
#pragma warning disable SYSLIB0057
            using System.Security.Cryptography.X509Certificates.X509Certificate signer =
                System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            if (!string.IsNullOrWhiteSpace(RequiredSignerThumbprint)
                && !string.Equals(signer.GetCertHashString(), RequiredSignerThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                LoggingService.LogWarning($"[UpdateManager] Signataire refusé (thumbprint {signer.GetCertHashString()}) pour '{Path.GetFileName(filePath)}'.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(RequiredSignerSubjectDn)
                && !string.Equals(signer.Subject, RequiredSignerSubjectDn, StringComparison.OrdinalIgnoreCase))
            {
                LoggingService.LogWarning($"[UpdateManager] Signataire refusé ({signer.Subject}) pour '{Path.GetFileName(filePath)}'.");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "UpdateManager.IsSignerPinned");
            return false;
        }
    }

    private static void TryLogSignerIdentity(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using System.Security.Cryptography.X509Certificates.X509Certificate signer =
                System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            LoggingService.LogInfo($"[UpdateManager] Signataire de '{Path.GetFileName(filePath)}' : {signer.Subject} (thumbprint {signer.GetCertHashString()}).");
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogWarning($"[UpdateManager] Identité du signataire illisible pour '{Path.GetFileName(filePath)}' : {ex.Message}");
        }
    }

    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr hFile;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr pgKnownSubject;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern uint WinVerifyTrust([In] IntPtr hwnd, [In] ref Guid pgActionId, [In] ref WinTrustFileInfo pWVTData);
}

internal class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;
}

internal class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; set; } = string.Empty;
}
