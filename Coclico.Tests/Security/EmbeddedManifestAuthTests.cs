using System.IO.Compression;
using System.Text.Json;
using Coclico.Security.Domain;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class EmbeddedManifestAuthTests
{
    private static byte[] BuildPayloadZip(TestWorld world, SignedIntegrityManifest signed, string? tamperFile = null, string? extraFile = null)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string file in Directory.EnumerateFiles(world.RootDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(world.RootDir, file).Replace('\\', '/');
                if (relative.Equals("integrity.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ZipArchiveEntry entry = archive.CreateEntry(relative);
                using var entryStream = entry.Open();
                byte[] content = File.ReadAllBytes(file);
                if (tamperFile != null && relative.Equals(tamperFile, StringComparison.OrdinalIgnoreCase))
                {
                    content = System.Text.Encoding.UTF8.GetBytes("TAMPERED");
                }

                entryStream.Write(content, 0, content.Length);
            }

            if (extraFile != null)
            {
                ZipArchiveEntry extra = archive.CreateEntry(extraFile);
                using var s = extra.Open();
                byte[] content = System.Text.Encoding.UTF8.GetBytes("unsigned extra file");
                s.Write(content, 0, content.Length);
            }

            ZipArchiveEntry manifestEntry = archive.CreateEntry("integrity.json");
            using var msEntry = manifestEntry.Open();
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(signed);
            msEntry.Write(manifestBytes, 0, manifestBytes.Length);
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task EmbeddedZipValidSignatureAndPayloadAuthenticates()
    {
        using var world = new TestWorld();
        world.WriteFile("app/Coclico.dll", "fake dll content");
        world.WriteFile("app/Coclico.exe", "fake exe content");
        SignedIntegrityManifest signed = world.BuildManifest(version: "2.3.0");
        world.SaveManifest(signed);

        byte[] zip = BuildPayloadZip(world, signed);

        var result = await ReleaseAuthentication.AuthenticateEmbeddedZipAsync(zip, world.Policy, "v2.3.0");

        Assert.True(result.IsAuthenticated, result.Reason);
        Assert.NotNull(result.VerifiedManifest);
    }

    [Fact]
    public async Task EmbeddedZipWrongReleaseTagIsRejected()
    {
        using var world = new TestWorld();
        world.WriteFile("app/Coclico.dll", "content");
        SignedIntegrityManifest signed = world.BuildManifest(version: "2.3.0");
        world.SaveManifest(signed);
        byte[] zip = BuildPayloadZip(world, signed);

        var result = await ReleaseAuthentication.AuthenticateEmbeddedZipAsync(zip, world.Policy, "v9.9.9");

        Assert.False(result.IsAuthenticated);
        Assert.Contains("does not match release tag", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbeddedZipTamperedPayloadIsRejected()
    {
        using var world = new TestWorld();
        world.WriteFile("app/Coclico.dll", "original content");
        SignedIntegrityManifest signed = world.BuildManifest(version: "2.3.0");
        world.SaveManifest(signed);
        byte[] zip = BuildPayloadZip(world, signed, tamperFile: "app/Coclico.dll");

        var result = await ReleaseAuthentication.AuthenticateEmbeddedZipAsync(zip, world.Policy, "v2.3.0");

        Assert.False(result.IsAuthenticated);
        Assert.Contains("payload", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbeddedZipUnsignedExtraFileIsRejected()
    {
        using var world = new TestWorld();
        world.WriteFile("app/Coclico.dll", "content");
        SignedIntegrityManifest signed = world.BuildManifest(version: "2.3.0");
        world.SaveManifest(signed);
        byte[] zip = BuildPayloadZip(world, signed, extraFile: "app/evil.dll");

        var result = await ReleaseAuthentication.AuthenticateEmbeddedZipAsync(zip, world.Policy, "v2.3.0");

        Assert.False(result.IsAuthenticated);
        Assert.Contains("not covered by the signed manifest", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbeddedZipWrongSigningKeyIsRejected()
    {
        using var world = new TestWorld();
        using var otherWorld = new TestWorld();
        world.WriteFile("app/Coclico.dll", "content");
        SignedIntegrityManifest signed = world.BuildManifest(version: "2.3.0");
        world.SaveManifest(signed);
        byte[] zip = BuildPayloadZip(world, signed);

        var result = await ReleaseAuthentication.AuthenticateEmbeddedZipAsync(zip, otherWorld.Policy, "v2.3.0");

        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public async Task EmbeddedZipUnsignedManifestIsRejected()
    {
        using var world = new TestWorld();
        world.WriteFile("app/Coclico.dll", "content");
        SignedIntegrityManifest unsigned = world.BuildManifest(version: "2.3.0", sign: false);
        byte[] zip = BuildPayloadZip(world, unsigned);

        var result = await ReleaseAuthentication.AuthenticateEmbeddedZipAsync(zip, world.Policy, "v2.3.0");

        Assert.False(result.IsAuthenticated);
    }
}
