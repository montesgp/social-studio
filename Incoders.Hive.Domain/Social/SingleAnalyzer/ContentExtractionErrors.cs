using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.SingleAnalyzer;

/// <summary>
/// Stable error codes for content extraction failures.
/// Codes match the spec (CAP1-EC-01 through CAP4-MUST-07).
/// </summary>
public static class ContentExtractionErrors
{
    /// <summary>The Apify actor returned an empty dataset (no items).</summary>
    public static readonly Error EmptyResponse = Error.Failure(
        "ContentExtraction.EmptyResponse",
        "content_extraction.empty_response");

    /// <summary>The URL platform is not supported by any registered adapter.</summary>
    public static readonly Error UnsupportedPlatform = Error.Failure(
        "ContentExtraction.UnsupportedPlatform",
        "content_extraction.unsupported_platform");

    /// <summary>The submitted URL is invalid or unparseable.</summary>
    public static readonly Error InvalidUrl = Error.Validation(
        "ContentExtraction.InvalidUrl",
        "content_extraction.invalid_url");

    /// <summary>The actor response contained items but mapping produced only partial data.</summary>
    public static readonly Error PartialData = Error.Failure(
        "Snapshot.PartialData",
        "snapshot.partial_data");

    /// <summary>
    /// The provider returned a dataset item flagged with its own "error"/"errorDescription"
    /// (e.g. Apify's <c>restricted_page</c> for a private/age-gated/region-locked post) instead of
    /// content. The URL is reachable but the source itself refused full access.
    /// </summary>
    public static readonly Error RestrictedContent = Error.Failure(
        "ContentExtraction.RestrictedContent",
        "content_extraction.restricted_content");
}
