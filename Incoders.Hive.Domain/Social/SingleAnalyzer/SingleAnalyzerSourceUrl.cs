using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.SingleAnalyzer;

public sealed record SingleAnalyzerSourceUrl(
    string OriginalUrl,
    string NormalizedUrl,
    string PlatformKey)
{
    public const int MaxLength = 2_048;

    public static Result<SingleAnalyzerSourceUrl> Create(string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return Result.Failure<SingleAnalyzerSourceUrl>(SingleAnalyzerErrors.UrlRequired);
        }

        var original = sourceUrl.Trim();
        if (original.Length > MaxLength
            || !Uri.TryCreate(original, UriKind.Absolute, out var uri)
            || !IsSupportedScheme(uri.Scheme))
        {
            return Result.Failure<SingleAnalyzerSourceUrl>(SingleAnalyzerErrors.UrlInvalid);
        }

        var normalized = Normalize(uri);
        var platform = DetectPlatform(uri.Host);

        return Result.Success(new SingleAnalyzerSourceUrl(original, normalized, platform));
    }

    private static bool IsSupportedScheme(string scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if ((builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80)
            || (builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443))
        {
            builder.Port = -1;
        }

        return builder.Uri.AbsoluteUri;
    }

    private static string DetectPlatform(string host)
    {
        var normalizedHost = host.Trim().ToLowerInvariant();
        if (normalizedHost.StartsWith("www.", StringComparison.Ordinal))
        {
            normalizedHost = normalizedHost[4..];
        }

        return normalizedHost switch
        {
            "tiktok.com" or "vm.tiktok.com" or "m.tiktok.com" => "tiktok",
            "instagram.com" or "instagr.am" => "instagram",
            "youtube.com" or "youtu.be" or "youtube-nocookie.com" => "youtube",
            "x.com" or "twitter.com" => "x",
            "facebook.com" or "fb.com" or "fb.watch" or "m.facebook.com" => "facebook",
            "linkedin.com" or "lnkd.in" => "linkedin",
            _ when normalizedHost.EndsWith(".tiktok.com", StringComparison.Ordinal) => "tiktok",
            _ when normalizedHost.EndsWith(".instagram.com", StringComparison.Ordinal) => "instagram",
            _ when normalizedHost.EndsWith(".youtube.com", StringComparison.Ordinal) => "youtube",
            _ when normalizedHost.EndsWith(".x.com", StringComparison.Ordinal) => "x",
            _ when normalizedHost.EndsWith(".twitter.com", StringComparison.Ordinal) => "x",
            _ when normalizedHost.EndsWith(".facebook.com", StringComparison.Ordinal) => "facebook",
            _ when normalizedHost.EndsWith(".linkedin.com", StringComparison.Ordinal) => "linkedin",
            _ => "unknown"
        };
    }
}
