namespace SocialStudio.Infrastructure.Social.Providers.Apify;

/// <summary>
/// PLATFORM-level cost and duration guards applied to every niche-search actor run, bound from the
/// configuration section <c>Apify:NicheSearchGuards</c>.
///
/// WHY THESE EXIST. Apify's <c>run-sync-get-dataset-items</c> endpoint takes the run input from the
/// POSTed body: whatever the operator saved in the Apify dashboard does NOT apply to an API run, and
/// any field we omit falls back to the ACTOR's schema default. Real runs proved the consequence — a
/// dashboard "max posts per hashtag = 5" produced runs of 202 and 120 items, because the per-page field
/// we send is not the one that actor uses, so our value was ignored outright.
///
/// The guards below are query parameters of the RUN ENDPOINT, not actor input, so they hold regardless
/// of which actor the operator picked and regardless of that actor's input schema. They are the only
/// limits that are safe against an unknown field name.
///
/// A per-actor input-field mapping (so the actor's OWN per-hashtag cap is honoured) may still be worth
/// adding later; until then these platform guards are what makes an unknown field name affordable.
/// </summary>
public sealed class ApifyNicheSearchGuardOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Apify:NicheSearchGuards";

    /// <summary>
    /// Caps the TOTAL cost of a single run across all Apify pricing models (pay-per-result,
    /// pay-per-event, compute units). This is the real money guard: it does not depend on the actor's
    /// pricing model, its input schema, or how many items it decides to scrape.
    ///
    /// Default 0.25 USD — deliberately cheap. A discovery run is a shortlist builder, not a bulk
    /// scrape, and the environment this ships to is a test one where a runaway run has already cost
    /// real money. Raise it in configuration once a run's real cost is known.
    /// </summary>
    public decimal MaxTotalChargeUsd { get; set; } = 0.25m;

    /// <summary>
    /// Hard run duration limit in seconds, enforced by the Apify platform.
    ///
    /// Default 90 s — comfortably below the 120 s timeout of our own HttpClient. That ordering is the
    /// whole point: if OUR client gives up first (timeout, or a browser/proxy aborting the request),
    /// the run is orphaned and keeps billing with nobody waiting for it. At 90 s the platform stops the
    /// run before we ever stop waiting, so an abandoned request has a bounded cost.
    /// </summary>
    public int RunTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Caps the number of dataset items CHARGED for pay-per-result actors.
    ///
    /// Apify is explicit that this does NOT guarantee the actor returns only this many items, so it is
    /// a charge cap and never a hard item limit — <see cref="MaxTotalChargeUsd"/> and
    /// <see cref="RunTimeoutSeconds"/> are what actually bound a runaway run.
    ///
    /// Default 50 — above the largest shortlist the discovery UI asks for in practice (20), with room
    /// for the hashtag split, and far below the 202 items a real unguarded run billed for.
    /// </summary>
    public int MaxItems { get; set; } = 50;

    /// <summary>
    /// Renders the guards as run-endpoint query parameters, each leading with <c>&amp;</c> so it can be
    /// appended to a URL that already carries the token.
    ///
    /// A non-positive value means "do not send this guard" — the documented way to disable one
    /// individually without inventing a sentinel. Values are formatted with the invariant culture so a
    /// comma-decimal host locale can never emit <c>0,25</c> in a URL.
    /// </summary>
    public string ToQueryString()
    {
        var query = new System.Text.StringBuilder();

        if (MaxTotalChargeUsd > 0m)
        {
            query.Append("&maxTotalChargeUsd=")
                .Append(MaxTotalChargeUsd.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (RunTimeoutSeconds > 0)
        {
            query.Append("&timeout=")
                .Append(RunTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (MaxItems > 0)
        {
            query.Append("&maxItems=")
                .Append(MaxItems.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return query.ToString();
    }
}
