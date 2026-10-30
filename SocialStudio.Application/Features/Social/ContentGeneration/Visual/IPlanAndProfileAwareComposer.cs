using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Optional capability for format composers that can consume BOTH a pre-computed
/// <see cref="CarouselBackgroundPlan"/> (WU-4 coherence refactor) AND a resolved
/// <see cref="ClientAestheticProfile"/> (WU-3 / exact-client-typography) in the SAME call.
///
/// Without this combined capability, <see cref="VisualCompositionService"/> could dispatch to
/// <see cref="IBackgroundPlanAwareComposer"/> OR <see cref="IClientAestheticAwareComposer"/> but
/// never both together — so a ClientAesthetic carousel job (which always supplies a plan) silently
/// lost its profile, and the client's real observed typography never reached the composed spec.
/// Bug reference: GitHub issue #81.
///
/// Both parameters are independently nullable: <c>plan: null, profile: null</c> is
/// byte-identical to the pre-existing plan-less, profile-less legacy path.
/// </summary>
internal interface IPlanAndProfileAwareComposer
{
    /// <summary>
    /// Returns an ordered list of composition specs, using <paramref name="plan"/> for per-slide
    /// background selection (when supplied) and <paramref name="profile"/> for per-slide palette
    /// and typography resolution (when supplied).
    /// </summary>
    IReadOnlyList<VisualCompositionSpec> ComposeMany(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred,
        CarouselBackgroundPlan? plan,
        ClientAestheticProfile? profile);
}
