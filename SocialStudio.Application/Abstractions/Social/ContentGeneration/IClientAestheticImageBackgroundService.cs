using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for the creative-background producer.
/// Orchestrates: prompt-write → image-render → durable-store → ForcedDefault, for the subset
/// of "creative" slides in a ClientAesthetic carousel.
/// Registered as a NULLABLE dependency on the processor: when absent (no IMAGE route configured,
/// or policy ≠ ClientAesthetic), the existing CSS designer path runs unchanged — rollback-safe.
/// Returns ONLY the slides that succeeded; absent slides keep their flat CSS selection.
/// Refs: design §3.3; tasks 1.3.
/// </summary>
public interface IClientAestheticImageBackgroundService
{
    /// <summary>
    /// Builds creative raster backgrounds for the supplied creative slide indices.
    /// Returns a map of 1-based slide index → <see cref="BackgroundSelection.ForcedDefault"/>
    /// for every slide that succeeded. Slides absent from the map degrade to the CSS designer.
    /// </summary>
    Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>> BuildCreativeBackgroundsAsync(
        ClientAestheticImageRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Input to <see cref="IClientAestheticImageBackgroundService"/>.
/// Contains only primitives, Domain types, and the Application VO <see cref="ClientAestheticProfile"/>.
/// </summary>
public sealed record ClientAestheticImageRequest(
    Guid OrganizationId,
    Guid? ClientId,
    Guid JobId,
    Guid FormatResultId,
    string FormatKey,
    int SlideCount,
    /// <summary>
    /// 1-based slide indices already capped to <c>CreativeSlideIndexResolver.MaxCreativeSlidesPerPackage</c>.
    /// </summary>
    IReadOnlyList<int> CreativeSlideIndices,
    ClientAestheticProfile Aesthetic,
    /// <summary>Per-slide generated copy summaries for on-topic image prompting (index 0 = slide 1).</summary>
    IReadOnlyList<string> PerSlideCopySummaries);
