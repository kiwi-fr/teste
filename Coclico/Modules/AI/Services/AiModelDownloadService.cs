using System.IO;
using System.Net.Http;
using Coclico.Security.Infrastructure;

namespace Coclico.Services.AI;

internal sealed record AiModelDownloadProgress(int Percent, string Message);

internal static class AiModelDownloadService
{
    public static async Task DownloadGgufModelAsync(
        AiModelInfo modelInfo,
        string destinationPath,
        IProgress<AiModelDownloadProgress> progress,
        CancellationToken ct = default)
    {
        string? url = modelInfo.DownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Aucune URL de téléchargement disponible pour ce modèle.");
        }

        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Téléchargement refusé : seule une URL HTTPS est autorisée.");
        }

        string? dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        string tmp = destinationPath + ".tmp";
        try
        {
            using var http = new HttpClient(new OutboundDomainGuardHandler());
            http.Timeout = TimeSpan.FromHours(2);
            using HttpResponseMessage resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            _ = resp.EnsureSuccessStatusCode();
            long? totalBytes = resp.Content.Headers.ContentLength;
            using Stream s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int read;
            DateTime lastReport = DateTime.MinValue;
            while ((read = await s.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                totalRead += read;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    DateTime now = DateTime.UtcNow;
                    if ((now - lastReport).TotalMilliseconds > 200 || totalRead == totalBytes.Value)
                    {
                        lastReport = now;
                        double pct = (double)totalRead / totalBytes.Value * 100.0;
                        progress.Report(new AiModelDownloadProgress(
                            (int)Math.Clamp(pct, 0, 100),
                            $"Téléchargement de {modelInfo.DisplayName} : {pct:F0}% ({totalRead / (1024 * 1024)} Mo / {totalBytes.Value / (1024 * 1024)} Mo)"));
                    }
                }
            }

            fs.Close();

            if (!string.IsNullOrWhiteSpace(modelInfo.Sha256))
            {
                string actualHash;
                FileStream hashStream = File.OpenRead(tmp);
                await using (hashStream.ConfigureAwait(false))
                {
                    actualHash = Convert.ToHexString(
                        await System.Security.Cryptography.SHA256.HashDataAsync(hashStream, ct).ConfigureAwait(false));
                }
                string expectedHash = modelInfo.Sha256.Replace(" ", string.Empty).Replace("-", string.Empty);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Somme de contrôle SHA-256 invalide pour {modelInfo.Id}.");
                }
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tmp, destinationPath);
            MarkOfTheWeb.Apply(destinationPath);
        }
        catch
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                LoggingService.LogException(cleanupEx, "AiModelDownloadService.Cleanup");
            }

            throw;
        }
    }
}
