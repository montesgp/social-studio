using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.NicheDiscovery;

/// <summary>
/// Domain error constants for the NicheDiscovery capability. Codes follow
/// <c>social.niche_discovery.{reason}</c>, i18n keys follow
/// <c>social.niche_discovery.errors.{reason}</c> (design §10).
/// </summary>
public static class NicheDiscoveryErrors
{
    public static readonly Error MarketUnknown = Error.NotFound(
        "social.niche_discovery.market_unknown",
        "social.niche_discovery.errors.market_unknown");

    public static readonly Error MarketInactive = Error.Conflict(
        "social.niche_discovery.market_inactive",
        "social.niche_discovery.errors.market_inactive");

    public static readonly Error WindowOutOfRange = Error.Validation(
        "social.niche_discovery.window_out_of_range",
        "social.niche_discovery.errors.window_out_of_range");

    /// <summary>Malformed <c>MarketCode</c> input (design §4) — not the catalog-lookup failure <see cref="MarketUnknown"/>.</summary>
    public static readonly Error MarketCodeInvalid = Error.Validation(
        "social.niche_discovery.market_code_invalid",
        "social.niche_discovery.errors.market_code_invalid");

    public static readonly Error DepthUnknown = Error.Validation(
        "social.niche_discovery.depth_unknown",
        "social.niche_discovery.errors.depth_unknown");

    public static readonly Error ContentFormatInvalid = Error.Validation(
        "social.niche_discovery.content_format_invalid",
        "social.niche_discovery.errors.content_format_invalid");

    public static readonly Error NetworkUnsupported = Error.Validation(
        "social.niche_discovery.network_unsupported",
        "social.niche_discovery.errors.network_unsupported");

    /// <summary>
    /// No enabled origin route resolved for a stage. Reported as <see cref="DiscoveryRunStatus.Failed"/>
    /// with this reason so it is distinguishable from a run that ran and found zero opportunities (ND-02).
    /// </summary>
    public static readonly Error OriginUnconfigured = Error.Failure(
        "social.niche_discovery.origin_unconfigured",
        "social.niche_discovery.errors.origin_unconfigured");

    public static readonly Error ProviderQuotaExhausted = Error.Failure(
        "social.niche_discovery.provider_quota_exhausted",
        "social.niche_discovery.errors.provider_quota_exhausted");

    public static readonly Error ProviderUnavailable = Error.Failure(
        "social.niche_discovery.provider_unavailable",
        "social.niche_discovery.errors.provider_unavailable");

    public static readonly Error RunNotFound = Error.NotFound(
        "social.niche_discovery.run_not_found",
        "social.niche_discovery.errors.run_not_found");

    public static readonly Error RunAlreadyRunning = Error.Conflict(
        "social.niche_discovery.run_already_running",
        "social.niche_discovery.errors.run_already_running");

    public static readonly Error OpportunityNotFound = Error.NotFound(
        "social.niche_discovery.opportunity_not_found",
        "social.niche_discovery.errors.opportunity_not_found");
}
