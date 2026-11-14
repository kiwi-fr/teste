using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Coclico.Services.AI;

internal sealed class MultiProviderClient
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly TimeSpan HeadersTimeout = TimeSpan.FromMinutes(2);

    public static bool IsLocalEndpoint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)) ||
            !uri.IsLoopback ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return string.IsNullOrEmpty(uri.AbsolutePath) || string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal);
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        string? endpoint,
        string systemPrompt,
        IReadOnlyList<(string User, string Ai)> conversationHistory,
        string userMessage,
        string? base64Image = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string baseUrl = string.IsNullOrWhiteSpace(endpoint)
            ? new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), 11434).Uri.ToString().TrimEnd('/')
            : endpoint.Trim().TrimEnd('/');
        if (!IsLocalEndpoint(baseUrl))
        {
            yield return "Ollama doit être configuré sur la machine locale (adresse de boucle locale).";
            yield break;
        }

        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach ((string user, string assistant) in conversationHistory)
        {
            messages.Add(new { role = "user", content = user });
            messages.Add(new { role = "assistant", content = assistant });
        }

        var userPayload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["role"] = "user",
            ["content"] = userMessage
        };
        if (!string.IsNullOrEmpty(base64Image))
        {
            userPayload["images"] = new[] { base64Image };
        }
        messages.Add(userPayload);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { model = modelId, messages, stream = true }), Encoding.UTF8, "application/json")
        };

        using var headersCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headersCts.CancelAfter(HeadersTimeout);

        HttpResponseMessage? response = null;
        bool connectionFailed = false;
        try
        {
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersCts.Token).ConfigureAwait(false);
            headersCts.CancelAfter(Timeout.InfiniteTimeSpan);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            connectionFailed = true;
        }
        catch (HttpRequestException)
        {
            connectionFailed = true;
        }

        if (connectionFailed || response is null)
        {
            yield return "Impossible de joindre Ollama sur la machine locale. Vérifiez qu'Ollama est installé et démarré.";
            yield break;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                yield return $"Le modèle Ollama « {modelId} » est introuvable. Téléchargez-le avec `ollama pull {modelId}`.";
                yield break;
            }

            if (!response.IsSuccessStatusCode)
            {
                yield return $"Ollama a retourné une erreur HTTP {(int)response.StatusCode}.";
                yield break;
            }

            Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("error", out JsonElement error))
                {
                    yield return $"Ollama : {error.GetString() ?? "erreur inconnue"}";
                    yield break;
                }

                if (document.RootElement.TryGetProperty("message", out JsonElement message) &&
                    message.TryGetProperty("content", out JsonElement content) &&
                    content.GetString() is { Length: > 0 } token)
                {
                    yield return token;
                }
            }
            }
        }
    }
}
