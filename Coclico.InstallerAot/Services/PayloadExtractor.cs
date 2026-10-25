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

    public static void Extract(string destinationDir, IProgress<double>? progress = null)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException(
                $"Ressource embarquee '{PayloadResourceName}' introuvable. " +
                "Recompilez avec payload.zip present dans le dossier du projet.");

        string extractDir = Path.Join(Path.GetTempPath(), "coclico-payload-extract");
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        try
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(extractDir, overwriteFiles: true);
            }

            foreach (string sourceDir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Join(destinationDir, Path.GetRelativePath(extractDir, sourceDir)));
            }

            string[] files = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            long totalBytes = 0;
            foreach (string file in files)
            {
                totalBytes += new FileInfo(file).Length;
            }

            long copied = 0;
            foreach (string file in files)
            {
                File.Copy(file, Path.Join(destinationDir, Path.GetRelativePath(extractDir, file)), overwrite: true);
                copied += new FileInfo(file).Length;
                progress?.Report(totalBytes > 0 ? (double)copied / totalBytes * 100.0 : 100.0);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, recursive: true);
                }
            }
            catch { }
        }
    }
}
