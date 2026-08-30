using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// CASE C strategy: aesthetic guidance from profile-analysis text descriptors.
/// When descriptors are present, each slide receives ClientAesthetic(descriptors) so the AI
/// designer can generate backgrounds within the client's known aesthetic style.
/// When descriptors are empty (no analysis exists), degrades to Creative for all slides
/// and logs a warning — pipeline continues uninterrupted (BSP-04-b).
/// Refs: design §4.3; BSP-04.
/// </summary>
public sealed class ClientAestheticStrategy : IBackgroundSourcingStrategy
{
    private readonly ILogger<ClientAestheticStrategy> _logger;

    public ClientAestheticStrategy(ILogger<ClientAestheticStrategy> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public BackgroundSourcingPolicy Policy => BackgroundSourcingPolicy.ClientAesthetic;

    /// <inheritdoc />
    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request)
    {
        if (request.ClientAestheticDescriptors.Count == 0)
        {
            // Degrade to Creative — no profile analysis data available (BSP-04-b)
            _logger.LogWarning(
                "CASE C background strategy: no aesthetic descriptors available for format {FormatKey}. " +
                "Degrading to AI-creative (CASE B equivalent). " +
                "Ensure a completed ProfileAiAnalysis exists for the client.",
                request.FormatKey);

            var creativeSlides = Enumerable.Range(0, request.SlideCount)
                .Select<int, BackgroundSelection>(_ => new BackgroundSelection.Creative())
                .ToList();

            return new CarouselBackgroundPlan(Policy, creativeSlides, request.SeedToken);
        }

        // All slides receive the same descriptors as aesthetic context for the AI designer
        var aestheticSlides = Enumerable.Range(0, request.SlideCount)
            .Select<int, BackgroundSelection>(_ =>
                new BackgroundSelection.ClientAesthetic(request.ClientAestheticDescriptors))
            .ToList();

        return new CarouselBackgroundPlan(Policy, aestheticSlides, request.SeedToken);
    }
}
