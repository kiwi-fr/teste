using System.IO;

namespace Coclico.Services;

internal static class Guard
{
    public static void NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("La valeur ne peut pas être vide.", paramName);
        }
    }

    public static string EnsurePathInside(string baseDir, string path, string paramName)
    {
        NotNullOrWhiteSpace(baseDir, nameof(baseDir));
        NotNullOrWhiteSpace(path, paramName);

        string fullBase;
        string fullPath;
        try
        {
            fullBase = Path.GetFullPath(baseDir);
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("Chemin invalide.", paramName, ex);
        }

        string normalizedBase = fullBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Le chemin '{path}' sort du répertoire autorisé '{baseDir}'.", paramName);
        }

        return fullPath;
    }

    public static void EnsureFileSizeLimit(string path, long maxBytes, string paramName)
    {
        NotNullOrWhiteSpace(path, paramName);
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("Chemin invalide.", paramName, ex);
        }

        if (info.Exists && info.Length > maxBytes)
        {
            throw new IOException($"Le fichier '{path}' dépasse la taille maximale autorisée ({info.Length} > {maxBytes} octets).");
        }
    }

    public static Uri EnsureHttpsUrlOnAllowedHosts(string? url, IEnumerable<string> allowedHosts, string paramName)
    {
        NotNullOrWhiteSpace(url, paramName);

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException($"URL absolue invalide : '{url}'.", paramName);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Schéma HTTPS requis, reçu '{uri.Scheme}'.", paramName);
        }

        foreach (string host in allowedHosts)
        {
            if (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }
        }

        throw new ArgumentException($"Hôte non autorisé : '{uri.Host}'.", paramName);
    }
}
