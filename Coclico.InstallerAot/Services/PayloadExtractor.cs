using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Coclico.InstallerAot.Services;

public static class PayloadExtractor
{
    private const string PayloadResourceName = "Coclico.payload.zip";

    public static bool HasPayload()
        => Assembly.GetExecutingAssembly().GetManifestResourceInfo(PayloadResourceName) is not null;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SecurityCodeScan", "SCS0018",
        Justification = "payload.zip embarque a la compilation, non controle par l'utilisateur; prefixe verifie avant File.Create")]
    public static void Extract(string destinationDir, IProgress<double>? progress = null)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException(
                $"Ressource embarquee '{PayloadResourceName}' introuvable. " +
                "Recompilez avec payload.zip present dans le dossier du projet.");
        long totalBytes = 0;
        using (var sizeArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            foreach (var entry in sizeArchive.Entries)
            {
                totalBytes += entry.Length;
            }
        }

        long copied = 0;
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.Combine(destinationDir, entry.FullName);
            var fullDest = Path.GetFullPath(destinationPath);
            if (!fullDest.StartsWith(Path.GetFullPath(destinationDir), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Entree suspecte dans le payload : {entry.FullName}");
            }

            var dir = Path.GetDirectoryName(fullDest);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (entry.Length == 0 && string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            using (var input = entry.Open())
            using (var output = File.Create(fullDest))
            {
                input.CopyTo(output);
            }

            copied += entry.Length;
            progress?.Report(totalBytes > 0 ? (double)copied / totalBytes * 100.0 : 100.0);
        }
    }
}
