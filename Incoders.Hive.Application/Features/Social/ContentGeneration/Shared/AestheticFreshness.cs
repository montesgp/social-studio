using Incoders.Hive.Application.Abstractions.Social;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Shared constants and pure decision logic for the aesthetic-data freshness and staleness
/// checks. Refs: design §4, OQ-1; CAG-CG-03; aesthetic-freshness-versioning.
/// </summary>
public static class AestheticFreshness
{
    /// <summary>
    /// System-wide default staleness threshold in days.
    /// A client-level <c>AestheticRefreshThresholdDays</c> override takes precedence when set.
    /// Effective threshold = <c>client.AestheticRefreshThresholdDays ?? DefaultStalenessThresholdDays</c>.
    /// </summary>
    public const int DefaultStalenessThresholdDays = 30;

    /// <summary>
    /// Current aesthetic-extractor schema/prompt version — the SINGLE shared source of truth.
    /// <c>MultimodalAestheticStyleExtractor.PromptVersion</c> (Infrastructure) references this exact
    /// constant so the two never drift. It doubles as the anti-loop mechanism for
    /// <see cref="Evaluate"/>: a stored version older than this triggers exactly one upgrade
    /// refresh — once re-analyzed at the current version, the schema-version branch never fires
    /// again for that client until a future version bump.
    ///
    /// <para>
    /// v4 (measured-typical-slide-count): the extractor now measures <c>typicalSlideCount</c> directly
    /// (median per-post slide count) instead of relying on <c>BackgroundPattern</c>'s length as a proxy.
    /// Bumped per the established convention (see v3's brandMark bump) so profiles captured before this
    /// field existed are treated as stale and eligible for re-analysis, rather than silently keeping the
    /// old proxy value forever.
    /// </para>
    ///
    /// <para>
    /// v5 (WU-6a, CAGH-PA-01/02): the extractor now detects a <c>markType</c> discriminator
    /// (<c>"text"</c>/<c>"icon"</c>/<c>"unknown"</c>) and, for text marks, the literal <c>text</c>
    /// signature string on <c>brandMark</c>. Bumped per the established convention so profiles
    /// captured before this change are treated as stale and eligible for re-analysis.
    /// </para>
    ///
    /// <para>
    /// v6 (WU-6b, CAGH-PA-03/04): the extractor now resolves an icon mark's source image index to
    /// its archived <c>StoragePath</c> (<c>SourceImagePath</c>) and captures a validated, normalised
    /// <c>BoundingBox</c>. Bumped per the same convention — a v5 profile has neither field, so it
    /// must be treated as stale and eligible for re-analysis, rather than permanently missing the
    /// crop-derivation inputs.
    /// </para>
    /// </summary>
    public const string CurrentSchemaVersion = "aesthetic-extractor/v6";

    /// <summary>
    /// Parses the trailing integer from an <c>"aesthetic-extractor/vN"</c> schema version string.
    /// Unknown, null, empty, or malformed input parses to <c>0</c> — treated as the oldest possible
    /// version, which triggers an upgrade <see cref="AestheticFreshnessVerdict.Refresh"/> in
    /// <see cref="Evaluate"/>.
    /// </summary>
    public static int ParseSchemaVersion(string? schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            return 0;
        }

        var slashIndex = schemaVersion.LastIndexOf('/');
        var tail = slashIndex >= 0 ? schemaVersion[(slashIndex + 1)..] : schemaVersion;

        if (tail.Length < 2 || (tail[0] != 'v' && tail[0] != 'V'))
        {
            return 0;
        }

        return int.TryParse(tail[1..], out var version) ? version : 0;
    }

    /// <summary>
    /// Pure decision function evaluated ONLY within the age-fresh window (i.e. after
    /// <c>ClientAestheticEnsureFreshService</c> has already determined the latest analysis is
    /// recent enough AND carries an aesthetic payload). Age-expiry and missing-analysis paths are
    /// decided upstream and always refresh, regardless of this verdict.
    ///
    /// <list type="bullet">
    /// <item><b>Stored schema version older than <see cref="CurrentSchemaVersion"/></b> →
    /// <see cref="AestheticFreshnessVerdict.Refresh"/> (schema upgrade; the newer extractor may
    /// populate fields the older one left null).</item>
    /// <item><b>Same version AND materially complete</b> (ColorPairings + BackgroundPattern +
    /// ReferenceImages all present) → <see cref="AestheticFreshnessVerdict.Fresh"/>.</item>
    /// <item><b>Same version AND incomplete</b> → <see cref="AestheticFreshnessVerdict.Incomplete"/>
    /// — already analyzed at the current/best extractor version; the caller MUST treat this the
    /// same as Fresh (no refresh) to avoid looping forever without new data. Only age expiry or a
    /// future version bump refreshes it.</item>
    /// </list>
    /// </summary>
    public static AestheticFreshnessVerdict Evaluate(AestheticFreshnessSnapshot snapshot)
    {
        var storedVersion = ParseSchemaVersion(snapshot.SchemaVersion);
        var currentVersion = ParseSchemaVersion(CurrentSchemaVersion);

        if (storedVersion < currentVersion)
        {
            return AestheticFreshnessVerdict.Refresh;
        }

        var isComplete = snapshot.HasColorPairings && snapshot.HasBackgroundPattern && snapshot.HasReferenceImages;
        return isComplete ? AestheticFreshnessVerdict.Fresh : AestheticFreshnessVerdict.Incomplete;
    }
}

/// <summary>
/// Verdict returned by <see cref="AestheticFreshness.Evaluate"/>. <see cref="Fresh"/> and
/// <see cref="Incomplete"/> both mean "do NOT enqueue a refresh" from the caller's perspective —
/// they are kept distinct only so incompleteness at the current schema version is observable
/// (diagnostics / tests) without triggering a refresh loop.
/// </summary>
public enum AestheticFreshnessVerdict
{
    Refresh,
    Fresh,
    Incomplete,
}
