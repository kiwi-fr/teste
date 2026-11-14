using System.Collections.Frozen;
using System.Net;

namespace Coclico.Security.Infrastructure;

internal static class OutboundDomainGuard
{
    private static readonly FrozenSet<string> AllowedHosts = new[]
    {
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "codeload.github.com",
        "api.nuget.org",
        "huggingface.co",
        "cdn-lfs.huggingface.co",
        "cdn-lfs-us-1.huggingface.co",
        "speed.cloudflare.com",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        if (uri.Host.Equals(IPAddress.Loopback.ToString(), StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return false;
        }

        return AllowedHosts.Contains(uri.Host);
    }
}

public sealed class OutboundDomainGuardHandler : DelegatingHandler
{
    public OutboundDomainGuardHandler()
        : base(new HttpClientHandler())
    {
    }

    public OutboundDomainGuardHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!OutboundDomainGuard.IsAllowed(request.RequestUri))
        {
            throw new HttpRequestException($"Domaine sortant non autorisé : {request.RequestUri?.Host}");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
