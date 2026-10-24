using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Coclico.InstallerAot.Services;

/// <summary>
/// Extrait le payload embarque (payload.zip) vers le dossier d'installation,
/// avec progression en pourcentage.
///
/// MEME PRINCIPE QUE VOTRE INSTALLEUR WPF ACTUEL : payload.zip est une
/// EmbeddedResource compilee DANS l'exe (LogicalName = "Coclico.payload.zip",
/// defini dans le .csproj). Le script build-installer.ps1 depose payload.zip
/// dans le dossier du projet avant la compilation — rien a changer cote script.
///
/// Compatibilite NativeAOT : System.IO.Compression (ZipArchive) est
/// compatible AOT/trimming, aucun code reflexif.
/// </summary>
public static class PayloadExtractor
{
    private const string PayloadResourceName = "Coclico.payload.zip";

    /// <summary>Vrai si le payload est bien embarque dans l'exe.</summary>
    public static bool HasPayload()
        => Assembly.GetExecutingAssembly().GetManifestResourceInfo(PayloadResourceName) is not null;

    /// <summary>
    /// Extrait tout le contenu du payload.zip embarque vers <paramref name="destinationDir"/>,
    /// en rapportant la progression (0-100) via <paramref name="progress"/>.
    /// </summary>
    public static void Extract(string destinationDir, IProgress<double>? progress = null)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException(
                $"Ressource embarquee '{PayloadResourceName}' introuvable. " +
                "Recompilez avec payload.zip present dans le dossier du projet.");

        // Premiere passe : taille totale pour calculer la progression
        long totalBytes = 0;
        using (var sizeArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            foreach (var entry in sizeArchive.Entries)
            {
                totalBytes += entry.Length;
            }
        }

        // Deuxieme passe : extraction reelle
        long copied = 0;
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.Combine(destinationDir, entry.FullName);

            // Securite : bloquer les chemins qui sortent du dossier cible (zip slip)
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
                continue; // entree de dossier
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
