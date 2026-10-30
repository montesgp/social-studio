namespace SocialStudio.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// The ONE pure dedup-identity function (D3, OQ-3), shared by the chain's in-run dedup and the
/// persisted <c>candidate_key</c> so the two can never disagree.
///
/// Normalization: lowercase scheme + host, drop a leading "www.", drop query and fragment, trim a
/// trailing slash, and prefix with the lowercased network key.
/// </summary>
public static class CandidateIdentity
{
    public static string From(string network, string contentUrl)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(contentUrl);

        var normalizedNetwork = network.Trim().ToLowerInvariant();
        var normalizedUrl = NormalizeUrl(contentUrl.Trim());

        return $"{normalizedNetwork}:{normalizedUrl}";
    }

    private static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // Not a well-formed absolute URL — fall back to a case-normalized literal rather than
            // throwing; dedup identity must never crash the chain over a malformed source payload.
            return url.ToLowerInvariant();
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var path = uri.AbsolutePath.TrimEnd('/');

        return $"{uri.Scheme.ToLowerInvariant()}://{host}{path}";
    }
}
