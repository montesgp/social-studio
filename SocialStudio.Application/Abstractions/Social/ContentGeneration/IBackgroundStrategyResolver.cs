using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port that maps a <see cref="BackgroundSourcingPolicy"/> to the correct
/// <see cref="IBackgroundSourcingStrategy"/> implementation.
///
/// Null or Unset policy → LegacyTierStrategy (backward-compatible default).
/// Unknown policy → LegacyTierStrategy (safe fallback).
///
/// CASE D extensibility: register a new IBackgroundSourcingStrategy with the new policy value
/// and it resolves automatically — no changes to existing A/B/C strategies or this interface.
/// Refs: design §4.2; BSP-06.
/// </summary>
public interface IBackgroundStrategyResolver
{
    /// <summary>
    /// Returns the strategy for <paramref name="policy"/>.
    /// Never returns null — falls back to the legacy strategy for null/Unset/unknown values.
    /// </summary>
    IBackgroundSourcingStrategy Resolve(BackgroundSourcingPolicy? policy);
}
