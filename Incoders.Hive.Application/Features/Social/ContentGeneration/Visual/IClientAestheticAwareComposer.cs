using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Optional capability for format composers that apply a <see cref="ClientAestheticProfile"/>
/// to per-slide composition — in particular the rotating colour pairing from
/// <see cref="ClientAestheticProfile.ColorPairings"/>.
///
/// WU-3 (client-aligned-carousel-generation): when a profile is supplied, the carousel composer
/// calls <c>CompositionDefaults.ResolvePalette(brand, profile, slideIndex, inferred)</c> so each
/// slide receives the pairing at <c>pairings[(slideIndex-1) % count]</c> instead of the
/// positional brand palette.
///
/// Composers that do not implement this interface fall back to the positional palette (cold-start
/// parity — CAC-BSP-04-a). Profile is nullable: null degrades to the same positional palette.
///
/// Refs: CAC-CG-02; design §3.3, §0 OQ-2.
/// </summary>
internal interface IClientAestheticAwareComposer
{
    /// <summary>
    /// Returns an ordered list of composition specs — one per slide — with each slide's palette
    /// resolved from <paramref name="profile"/>'s <c>ColorPairings</c> when present.
    /// </summary>
    IReadOnlyList<VisualCompositionSpec> ComposeMany(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred,
        ClientAestheticProfile? profile);
}
