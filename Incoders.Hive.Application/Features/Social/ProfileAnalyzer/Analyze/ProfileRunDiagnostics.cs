namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// Comprehensive diagnostics captured at the end of each profile analysis pipeline run.
/// Serialized as ProviderDiagnosticsJson on ALL terminal paths (Analyzed, Failed, partial).
///
/// PA-23 shape — fields:
///   actor_id, fallback_used, posts_received, posts_processed, degraded_post_count,
///   ai_provider_key, ai_model.
/// Fallback-specific additions (when fallback_used == true):
///   default_actor_error, backup_actor_id.
/// Failure-specific additions (when the run ends in Failed):
///   failure_reasons (list of all attempt error codes).
///
/// Serialized with camelCase (JsonSerializerDefaults.Web) → snake_case property names
/// are NOT required — the column stores JSON for internal diagnostics review, not a
/// public API contract.
/// </summary>
public sealed record ProfileRunDiagnostics
{
    /// <summary>
    /// The actor id that produced the successful (or last-attempted) scrape result.
    /// Empty string when no actor attempt was made (e.g., configuration missing).
    /// </summary>
    public string ActorId { get; init; } = string.Empty;

    /// <summary>True when the backup actor was used because the default actor failed.</summary>
    public bool FallbackUsed { get; init; }

    /// <summary>Total posts returned by the Apify actor before owner-filtering.</summary>
    public int PostsReceived { get; init; }

    /// <summary>Owner-filtered posts after excluding non-owner and degraded posts.</summary>
    public int PostsProcessed { get; init; }

    /// <summary>Posts excluded because _degraded == true in the actor response.</summary>
    public int DegradedPostCount { get; init; }

    /// <summary>AI provider key used (e.g. "openai", "claude"). Null when AI stage not reached.</summary>
    public string? AiProviderKey { get; init; }

    /// <summary>AI model selected for this run. Null when AI stage not reached.</summary>
    public string? AiModel { get; init; }

    // ── Fallback-specific fields (populated when FallbackUsed == true) ──────────

    /// <summary>Error code from the default actor attempt, when fallback was triggered.</summary>
    public string? DefaultActorError { get; init; }

    /// <summary>The backup actor id that was used after the default actor failed.</summary>
    public string? BackupActorId { get; init; }

    // ── Failure-specific fields (populated when run ends in Failed) ─────────────

    /// <summary>
    /// All attempt failure reasons, in attempt order.
    /// Populated when the run ends in Failed (both actors exhausted, or pipeline-level failure).
    /// </summary>
    public IReadOnlyList<string>? FailureReasons { get; init; }
}
