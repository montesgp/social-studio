namespace SocialStudio.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// Canonicalizes Instagram profile targets before persistence/scraping.
/// Share URLs commonly arrive with tracking query parameters; Apify profile actors
/// are safer when the application stores and forwards the profile-only target.
/// </summary>
internal static class InstagramProfileTargetNormalizer
{
    public static string Normalize(string profileUrlOrHandle)
    {
        if (string.IsNullOrWhiteSpace(profileUrlOrHandle))
        {
            return string.Empty;
        }

        var trimmed = profileUrlOrHandle.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && IsInstagramHost(uri.Host))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                var username = segments[0].Trim().Trim('@', '/');
                if (!string.IsNullOrWhiteSpace(username))
                {
                    return $"https://www.instagram.com/{username}/";
                }
            }
        }

        var queryStart = trimmed.IndexOfAny(['?', '#']);
        return (queryStart >= 0 ? trimmed[..queryStart] : trimmed).TrimEnd('/');
    }

    private static bool IsInstagramHost(string host) =>
        string.Equals(host, "instagram.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "www.instagram.com", StringComparison.OrdinalIgnoreCase);
}
