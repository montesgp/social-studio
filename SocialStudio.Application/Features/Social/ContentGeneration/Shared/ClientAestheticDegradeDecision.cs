using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Which deterministic faithful-flat degrade trigger fired for a slide, if any.
/// Design §1 (D1). Tokens are written verbatim onto <c>CompositionSpecEnvelope.DegradeReason</c>
/// (D2) so backend, tests and the Content Studio badge agree on one spelling.
/// </summary>
public enum SlideDegradeTrigger
{
    /// <summary>No degrade — the slide renders through its normal path.</summary>
    None = 0,

    /// <summary>
    /// Trigger A: the pattern calls this slide Photo, but no generated image reached it.
    /// </summary>
    PhotoImageUnavailable = 1,

    /// <summary>
    /// Trigger B: the AI designer produced nothing usable (error OR guardrail rejection) and no
    /// enforced background exists.
    /// </summary>
    DesignerUnusable = 2,
}

/// <summary>
/// Verbatim degrade-reason tokens written onto <c>CompositionSpecEnvelope.DegradeReason</c> (D2).
/// One place both the processor and its tests (and eventually the Content Studio badge) agree on
/// the exact spelling.
/// </summary>
public static class SlideDegradeReason
{
    /// <summary>Trigger A fired: <see cref="SlideDegradeTrigger.PhotoImageUnavailable"/>.</summary>
    public const string PhotoImageUnavailable = "photo-image-unavailable";

    /// <summary>Trigger B fired: <see cref="SlideDegradeTrigger.DesignerUnusable"/>.</summary>
    public const string DesignerUnusable = "designer-unusable";
}

/// <summary>
/// Pure, directly-testable predicates that decide whether a ClientAesthetic carousel slide degrades
/// to the deterministic palette-backed flat treatment instead of the AI-designer/scrim-card path.
/// Design §1 (D1). The processor branches ONLY on these calls — no degrade logic lives inline in
/// <c>ContentGenerationJobProcessor</c>.
/// </summary>
public static class ClientAestheticDegradeDecision
{
    /// <summary>
    /// Shared precondition for BOTH triggers. <paramref name="hasColorPairings"/> is required
    /// because the faithful treatment IS the pairings-backed flat treatment — without it there is
    /// nothing faithful to degrade to, so the legacy path must stand (cold-start parity).
    /// </summary>
    public static bool IsEligible(
        BackgroundSourcingPolicy? policy, bool isCarouselLayout, bool hasColorPairings) =>
        policy == BackgroundSourcingPolicy.ClientAesthetic
        && isCarouselLayout
        && hasColorPairings;

    /// <summary>
    /// Trigger A: the pattern calls this slide Photo, but no generated image reached it.
    /// </summary>
    public static SlideDegradeTrigger TriggerA(
        bool eligible, SlideBackgroundKind? mappedKind, bool hasGeneratedImage) =>
        eligible && mappedKind == SlideBackgroundKind.Photo && !hasGeneratedImage
            ? SlideDegradeTrigger.PhotoImageUnavailable
            : SlideDegradeTrigger.None;

    /// <summary>
    /// Trigger B: the designer produced nothing usable (error OR guardrail rejection) and no
    /// enforced background exists.
    /// </summary>
    public static SlideDegradeTrigger TriggerB(
        bool eligible, bool hasEnforcedBackground, bool hasUsableAiMarkup) =>
        eligible && !hasEnforcedBackground && !hasUsableAiMarkup
            ? SlideDegradeTrigger.DesignerUnusable
            : SlideDegradeTrigger.None;
}
