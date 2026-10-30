using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// CASE B strategy: fully AI-generated backgrounds using the brand palette as weighting guidance.
/// Every slide gets Creative — the AI designer generates freely, constrained by the weighted
/// palette (primary dominant, accent sparing) carried in BrandManualSnapshot.ColorPalette.
/// No client background set is used; no background templates are enforced (BSP-03).
/// Refs: design §4.3; BSP-03.
/// </summary>
public sealed class AiPaletteStrategy : IBackgroundSourcingStrategy
{
    /// <inheritdoc />
    public BackgroundSourcingPolicy Policy => BackgroundSourcingPolicy.AiPalette;

    /// <inheritdoc />
    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request)
    {
        // All slides are Creative — palette weighting is conveyed through BrandManualSnapshot
        // to the AI designer (AiVisualDesigner.BuildInputJson / BuildSystemPrompt), not here.
        // This strategy's only job is to set the per-slide selection to Creative.
        var perSlide = Enumerable.Range(0, request.SlideCount)
            .Select<int, BackgroundSelection>(_ => new BackgroundSelection.Creative())
            .ToList();

        return new CarouselBackgroundPlan(Policy, perSlide, request.SeedToken);
    }
}
