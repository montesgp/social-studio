using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.NicheDiscovery;

/// <summary>
/// Normalized <c>{language}-{REGION}</c> market identity (design §4). Carries no semantics beyond
/// string identity and its language part — <see cref="Language"/> is the ONLY part any algorithm
/// reads, which is what makes a country-named branch structurally impossible: no function ever
/// reads a market's identity, only its language.
/// </summary>
public readonly record struct MarketCode
{
    private MarketCode(string value, string language)
    {
        Value = value;
        Language = language;
    }

    /// <summary>Canonical form, e.g. <c>"es-AR"</c>.</summary>
    public string Value { get; }

    /// <summary>Lowercase ISO 639-1 language part, e.g. <c>"es"</c>. The only readable part beyond <see cref="Value"/>.</summary>
    public string Language { get; }

    public static Result<MarketCode> TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Failure<MarketCode>(NicheDiscoveryErrors.MarketCodeInvalid);
        }

        var parts = raw.Split('-');
        if (parts.Length != 2)
        {
            return Result.Failure<MarketCode>(NicheDiscoveryErrors.MarketCodeInvalid);
        }

        var languagePart = parts[0];
        var regionPart = parts[1];

        if (languagePart.Length != 2 || regionPart.Length != 2)
        {
            return Result.Failure<MarketCode>(NicheDiscoveryErrors.MarketCodeInvalid);
        }

        if (!IsAsciiLetters(languagePart) || !IsAsciiLetters(regionPart))
        {
            return Result.Failure<MarketCode>(NicheDiscoveryErrors.MarketCodeInvalid);
        }

        var language = languagePart.ToLowerInvariant();
        var region = regionPart.ToUpperInvariant();

        return Result.Success(new MarketCode($"{language}-{region}", language));
    }

    private static bool IsAsciiLetters(string value)
    {
        foreach (var c in value)
        {
            if (c is < 'A' or > 'z' || (c > 'Z' && c < 'a'))
            {
                return false;
            }
        }

        return true;
    }
}
