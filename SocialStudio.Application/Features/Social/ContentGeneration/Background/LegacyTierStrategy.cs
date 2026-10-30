using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// Legacy 3-tier background strategy: wraps <see cref="BackgroundSelector"/> to replicate
/// the current per-slide selection behavior as a named strategy.
/// This is the default adapter for UNSET / null policy clients — observable output is
/// identical to the pre-policy selection logic (BSP-05).
/// Refs: design §4.3; BSP-05.
/// </summary>
public sealed class LegacyTierStrategy : IBackgroundSourcingStrategy
{
    /// <inheritdoc />
    public BackgroundSourcingPolicy Policy => BackgroundSourcingPolicy.Unset;

    /// <inheritdoc />
    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request)
    {
        var templates = request.Brand.BackgroundTemplates;

        // Replicate per-slide selection: each slide independently calls BackgroundSelector.
        // The 3-tier result (ForcedDefault / References / Creative) is the same for every
        // slide when templates are unchanged, so the legacy path is coherent by definition.
        var perSlide = new BackgroundSelection[request.SlideCount];
        for (var i = 0; i < request.SlideCount; i++)
        {
            perSlide[i] = BackgroundSelector.Select(templates, request.FormatKey);
        }

        return new CarouselBackgroundPlan(Policy, perSlide, request.SeedToken);
    }
}
