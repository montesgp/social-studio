using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for background sourcing strategies.
/// Each implementation handles one <see cref="BackgroundSourcingPolicy"/> value and
/// produces a <see cref="CarouselBackgroundPlan"/> for a format before the per-slide loop.
///
/// Contract: no Infrastructure types (EF Core, Apify, ASP.NET) in parameters or return types.
/// All inputs arrive pre-resolved so strategies are pure (unit-testable without fakes/mocks).
/// Refs: design §4.1; BSP-01/02/03/04/05.
/// </summary>
public interface IBackgroundSourcingStrategy
{
    /// <summary>
    /// The policy case this strategy serves.
    /// Used by <see cref="IBackgroundStrategyResolver"/> to build the keyed dispatch map.
    /// </summary>
    BackgroundSourcingPolicy Policy { get; }

    /// <summary>
    /// Produces a carousel-level background plan for all slides of a format.
    /// Called ONCE per format per job, before the per-slide loop.
    /// The returned plan is always non-null; empty/degraded inputs produce a Creative plan.
    /// </summary>
    CarouselBackgroundPlan Plan(BackgroundPlanRequest request);
}
