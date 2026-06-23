using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Input request for <see cref="IBackgroundSourcingStrategy.Plan"/>.
/// All fields are Domain types — no Infrastructure dependencies.
/// Refs: design §4.1.
/// </summary>
/// <param name="Brand">
/// Snapshot of the client's brand manual (backgrounds + weighted palette).
/// </param>
/// <param name="FormatKey">
/// The content format key (e.g. "carousel", "story-single", "feed-single").
/// </param>
/// <param name="SlideCount">
/// Number of slides in the format. 1 for singles, N for carousels.
/// </param>
/// <param name="SeedToken">
/// Deterministic seed for rotation (job id as string; date fallback if empty).
/// Stable across retries of the same job (OQ-1/OQ-6).
/// </param>
/// <param name="ClientAestheticDescriptors">
/// Pre-resolved CASE C aesthetic descriptors from profile analysis.
/// Empty list when the policy is not ClientAesthetic or no analysis exists.
/// Passed pre-fetched so strategies remain pure (no repository dependency).
/// </param>
public sealed record BackgroundPlanRequest(
    BrandManualSnapshot Brand,
    string FormatKey,
    int SlideCount,
    string SeedToken,
    IReadOnlyList<string> ClientAestheticDescriptors);
