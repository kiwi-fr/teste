using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Coclico.Security.Domain;
using Coclico.Security.Infrastructure;

namespace Coclico.Services;

internal sealed class ReleaseAuthenticationResult
{
    public bool IsTrustedKeyConfigured { get; init; }

    public bool IsAuthenticated { get; init; }

    public int SecurityGeneration { get; init; }

    public string Reason { get; init; } = string.Empty;

    public SignedIntegrityManifest? VerifiedManifest { get; init; }
}

internal static class ReleaseAuthentication
{
    private const string ManifestAssetName = "integrity.json";

    private static readonly string[] GitHubTrustedHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    ];

    public const string PayloadZipResourceName = "Coclico.Installer.payload.zip";
    public const string ManifestEntryName = "integrity.json";

    public static Task<ReleaseAuthenticationResult> AuthenticateEmbeddedAsync(
        string installerPath,
        TrustPolicy trustPolicy,
        string? expectedTag,
        CancellationToken ct)
    {
        byte[]? zipBytes = EmbeddedResourceReader.ReadResource(installerPath, PayloadZipResourceName);
        if (zipBytes == null || zipBytes.Length == 0)
        {
            return Task.FromResult(new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = trustPolicy.Keys.Count > 0,
                IsAuthenticated = false,
                Reason = "installer has no embedded payload resource"
            });
        }

        return AuthenticateEmbeddedZipAsync(zipBytes, trustPolicy, expectedTag);
    }

    public static Task<ReleaseAuthenticationResult> AuthenticateEmbeddedZipAsync(
        byte[] zipBytes,
        TrustPolicy trustPolicy,
        string? expectedTag)
    {
        try
        {
            using var zipStream = new MemoryStream(zipBytes, writable: false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
            ZipArchiveEntry? entry = archive.GetEntry(ManifestEntryName);
            if (entry == null || entry.Length <= 0 || entry.Length > SecurityLimits.MaxManifestBytes)
            {
                return Task.FromResult(new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = trustPolicy.Keys.Count > 0,
                    IsAuthenticated = false,
                    Reason = "embedded integrity manifest is missing or too large"
                });
            }

            byte[] manifestBytes;
            using (var entryStream = entry.Open())
            {
                manifestBytes = new byte[entry.Length];
                int read = 0;
                while (read < manifestBytes.Length)
                {
                    int n = entryStream.Read(manifestBytes, read, manifestBytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }

                if (read != manifestBytes.Length)
                {
                    return Task.FromResult(new ReleaseAuthenticationResult
                    {
                        IsTrustedKeyConfigured = trustPolicy.Keys.Count > 0,
                        IsAuthenticated = false,
                        Reason = "embedded integrity manifest is truncated"
                    });
                }
            }

            SignedIntegrityManifest? signed = JsonSerializer.Deserialize<SignedIntegrityManifest>(manifestBytes, Options);
            if (signed?.Manifest == null)
            {
                return Task.FromResult(new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = trustPolicy.Keys.Count > 0,
                    IsAuthenticated = false,
                    Reason = "embedded manifest cannot be parsed"
                });
            }

            if (trustPolicy.Keys.Count == 0)
            {
                return Task.FromResult(new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = false,
                    IsAuthenticated = false,
                    SecurityGeneration = signed.Manifest.SecurityGeneration,
                    Reason = "no trusted key is configured, falling back to SHA-256 sums"
                });
            }

            if (!ManifestSigner.VerifySignature(signed, trustPolicy, out SecurityIncident incident, out string reason))
            {
                return Task.FromResult(new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    SecurityGeneration = signed.Manifest.SecurityGeneration,
                    Reason = $"embedded manifest rejected: {incident} {reason}"
                });
            }

            if (!string.Equals(signed.Manifest.Product, "Coclico", StringComparison.Ordinal))
            {
                return Task.FromResult(new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    SecurityGeneration = signed.Manifest.SecurityGeneration,
                    Reason = $"embedded manifest is for product '{signed.Manifest.Product}', not Coclico"
                });
            }

            if (!string.IsNullOrWhiteSpace(expectedTag)
                && !string.Equals(
                    signed.Manifest.ReleaseVersion.Trim().TrimStart('v', 'V'),
                    expectedTag.Trim().TrimStart('v', 'V'),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    SecurityGeneration = signed.Manifest.SecurityGeneration,
                    Reason = $"embedded manifest version '{signed.Manifest.ReleaseVersion}' does not match release tag '{expectedTag}'"
                });
            }

            if (!PayloadVerifier.VerifyZipAgainstManifest(zipBytes, signed.Manifest, out string payloadReason))
            {
                return Task.FromResult(new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    SecurityGeneration = signed.Manifest.SecurityGeneration,
                    Reason = $"embedded payload verification failed: {payloadReason}"
                });
            }

            return Task.FromResult(new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = true,
                IsAuthenticated = true,
                SecurityGeneration = signed.Manifest.SecurityGeneration,
                VerifiedManifest = signed,
                Reason = $"embedded manifest signed by {signed.Manifest.KeyId}, payload fully verified"
            });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            return Task.FromResult(new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = trustPolicy.Keys.Count > 0,
                IsAuthenticated = false,
                Reason = $"embedded manifest could not be read: {ex.Message}"
            });
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 8
    };

    public static async Task<ReleaseAuthenticationResult> AuthenticateAsync(
        HttpClient httpClient,
        GitHubRelease release,
        TrustPolicy trustPolicy,
        CancellationToken ct)
    {
        GitHubAsset? manifestAsset = release.Assets?.Find(a =>
            a.Name.Equals(ManifestAssetName, StringComparison.OrdinalIgnoreCase));

        if (manifestAsset == null)
        {
            if (trustPolicy.Keys.Count > 0)
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    Reason = "release has no signed integrity manifest"
                };
            }

            return new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = false,
                IsAuthenticated = false,
                Reason = "release has no signed manifest and no trusted key is configured, falling back to SHA-256 sums"
            };
        }

        if (manifestAsset.Size is <= 0 or > SecurityLimits.MaxManifestBytes)
        {
            return new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = true,
                IsAuthenticated = false,
                Reason = "signed manifest asset is not usable"
            };
        }

        Uri downloadUri;
        try
        {
            downloadUri = Guard.EnsureHttpsUrlOnAllowedHosts(
                manifestAsset.BrowserDownloadUrl,
                GitHubTrustedHosts,
                nameof(manifestAsset));
        }
        catch (ArgumentException)
        {
            return new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = true,
                IsAuthenticated = false,
                Reason = "signed manifest asset is not usable"
            };
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            byte[] manifestBytes = await httpClient.GetByteArrayAsync(downloadUri, cts.Token).ConfigureAwait(false);
            if (manifestBytes.Length > SecurityLimits.MaxManifestBytes)
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    Reason = "signed manifest exceeds the size limit"
                };
            }

            SignedIntegrityManifest? signed = JsonSerializer.Deserialize<SignedIntegrityManifest>(manifestBytes, Options);
            if (signed?.Manifest == null)
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    Reason = "signed manifest cannot be parsed"
                };
            }

            if (trustPolicy.Keys.Count == 0)
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = false,
                    IsAuthenticated = false,
                    SecurityGeneration = signed.Manifest.SecurityGeneration,
                    Reason = "no trusted key is configured, falling back to SHA-256 sums"
                };
            }

            if (!ManifestSigner.VerifySignature(signed, trustPolicy, out SecurityIncident incident, out string reason))
            {
                return new ReleaseAuthenticationResult
                {
                    IsTrustedKeyConfigured = true,
                    IsAuthenticated = false,
                    SecurityGeneration = signed.Manifest.SecurityGeneration,
                    Reason = $"manifest rejected: {incident} {reason}"
                };
            }

            return new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = true,
                IsAuthenticated = true,
                SecurityGeneration = signed.Manifest.SecurityGeneration,
                VerifiedManifest = signed,
                Reason = $"signed by {signed.Manifest.KeyId}"
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            return new ReleaseAuthenticationResult
            {
                IsTrustedKeyConfigured = true,
                IsAuthenticated = false,
                Reason = $"manifest could not be downloaded or read: {ex.Message}"
            };
        }
    }
}
