using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Optional capability for format composers that can consume a pre-computed
/// <see cref="CarouselBackgroundPlan"/> to drive per-slide background selection.
///
/// WU-4 coherence refactor: when the processor provides a plan, the composer reads
/// <c>plan.ForSlide(slideIndex)</c> instead of calling <c>BackgroundSelector.Select</c>
/// per slide — ensuring both call sites use the SAME background decision.
///
/// Composers that do not implement this interface receive the plan-less path (legacy behavior).
/// Refs: design §7.2; CG-COH-02.
/// </summary>
public interface IBackgroundPlanAwareComposer
{
    /// <summary>
    /// Returns an ordered list of composition specs, each using <c>plan.ForSlide(slideIndex)</c>
    /// for the slide's background selection instead of an independent selector call.
    /// </summary>
    IReadOnlyList<VisualCompositionSpec> ComposeMany(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred,
        CarouselBackgroundPlan plan);
}
