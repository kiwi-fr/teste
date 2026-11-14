using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Coclico.Security.Domain;

namespace Coclico.Security.Infrastructure;

public static class PayloadVerifier
{
    public static bool VerifyZipAgainstManifest(byte[] zipBytes, IntegrityManifest manifest, out string reason)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);
        ArgumentNullException.ThrowIfNull(manifest);

        try
        {
            using var zipStream = new MemoryStream(zipBytes, writable: false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

            if (!ArchiveGuard.IsEntryCountAllowed(archive.Entries.Count))
            {
                reason = $"archive has too many entries ({archive.Entries.Count})";
                return false;
            }

            var expected = new Dictionary<string, ManifestFileEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ManifestFileEntry entry in manifest.Files)
            {
                string normalized = entry.Path.Replace('\\', '/');
                if (!ArchiveGuard.IsSafeArchiveEntry(normalized))
                {
                    reason = $"manifest contains unsafe path '{entry.Path}'";
                    return false;
                }

                expected[normalized] = entry;
            }

            long accumulated = 0;
            foreach (ZipArchiveEntry zipEntry in archive.Entries)
            {
                string name = zipEntry.FullName.Replace('\\', '/');
                if (!ArchiveGuard.IsSafeArchiveEntry(name))
                {
                    reason = $"archive contains unsafe entry '{zipEntry.FullName}'";
                    return false;
                }

                if (name.Equals("integrity.json", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ArchiveGuard.IsEntrySizeAllowed(zipEntry.Length, accumulated))
                {
                    reason = $"archive entry '{name}' exceeds size limits";
                    return false;
                }

                accumulated += zipEntry.Length;

                if (!expected.TryGetValue(name, out ManifestFileEntry? expectedEntry))
                {
                    reason = $"archive entry '{name}' is not covered by the signed manifest";
                    return false;
                }

                if (zipEntry.Length != expectedEntry.Bytes)
                {
                    reason = $"size mismatch for '{name}' ({zipEntry.Length} != {expectedEntry.Bytes})";
                    return false;
                }

                string actualHash;
                using (Stream entryStream = zipEntry.Open())
                {
                    actualHash = Convert.ToHexString(SHA256.HashData(entryStream));
                }

                if (!string.Equals(actualHash, expectedEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"SHA-256 mismatch for '{name}'";
                    return false;
                }

                _ = expected.Remove(name);
            }

            if (expected.Count > 0)
            {
                reason = $"{expected.Count} signed file(s) missing from the archive (e.g. '{expected.Keys.First()}')";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            reason = $"archive could not be read: {ex.Message}";
            return false;
        }
    }
}
