using System.Text.RegularExpressions;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Application-layer guardrail for attribution/signature text in generated client deliverables.
/// Source-origin identities are valid analysis metadata and inspiration, but never attribution for
/// the target client's final content.
/// </summary>
internal static class AttributionPolicy
{
    public const string PromptConstraint =
        "Source-origin identity (source author, source URL, source channel, referent accounts, and competitor accounts) " +
        "is inspiration/metadata only. It must never be used as final attribution, footer, signature, watermark, " +
        "username, or handle. Final attribution must belong only to the target client/brand.";

    private static readonly Regex HandleRegex = new(@"@\w[\w\.\-_]{1,60}", RegexOptions.Compiled);

    public static string? ResolveSignature(BrandSnapshot brand, string? modelSignatureText)
    {
        ArgumentNullException.ThrowIfNull(brand);

        // Issue #70: conditional @handle signature, client-field-only. No attribution/signature
        // (deterministic Handle/notes/name, nor an AI-authored model signature) is ever emitted
        // unless the client explicitly opted in via Client.SignsOwnSlides. Plain boolean gate —
        // no tri-state, no profile-analysis detection. Existing clients default to false = not
        // signed (intended behavior change from the previous always-attempt-attribution default).
        if (brand.Client?.SignsOwnSlides != true)
        {
            return null;
        }

        // WU-5: ClientProfileSnapshot.Handle (from ClientOwnAccount) is the highest-priority
        // deterministic attribution — it is the client's own explicitly-registered handle,
        // which supersedes any handle extracted from brand notes or the client name.
        var deterministic = FirstNotBlank(
            brand.Client?.Handle,
            ExtractHandleFromNotes(brand.Brand?.Notes),
            brand.Client?.Name);

        if (!string.IsNullOrWhiteSpace(deterministic))
        {
            return deterministic.Trim();
        }

        var normalizedModelSignature = modelSignatureText?.Trim();
        return IsAllowedModelSignature(normalizedModelSignature, brand)
            ? normalizedModelSignature
            : null;
    }

    private static bool IsAllowedModelSignature(string? signature, BrandSnapshot brand)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var prohibited = ProhibitedTokens(brand);
        return !prohibited.Any(token => signature.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ProhibitedTokens(BrandSnapshot brand)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in brand.ReferentAccounts.Concat(brand.CompetitorAccounts))
        {
            AddToken(tokens, account.Handle);
            AddToken(tokens, account.Handle?.TrimStart('@'));
            AddToken(tokens, account.Label);
            AddToken(tokens, account.ProfileUrl);

            if (Uri.TryCreate(account.ProfileUrl, UriKind.Absolute, out var uri))
            {
                AddToken(tokens, uri.Host);
                AddToken(tokens, uri.Segments.LastOrDefault()?.Trim('/'));
            }
        }

        return tokens.ToList();
    }

    private static string? ExtractHandleFromNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var match = HandleRegex.Match(notes);
        return match.Success ? match.Value : null;
    }

    private static string? FirstNotBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void AddToken(HashSet<string> tokens, string? value)
    {
        var token = value?.Trim();
        if (!string.IsNullOrWhiteSpace(token) && token.Length >= 2)
        {
            tokens.Add(token);
        }
    }
}
