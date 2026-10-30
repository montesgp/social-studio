using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Domain.Social.ContentGeneration;

/// <summary>
/// Carousel-level background plan computed ONCE per format per job, before the per-slide loop.
/// Each entry in <see cref="PerSlide"/> maps to a 1-based slide index (index 0 = slide 1).
/// Refs: design §3.5; OQ-1/OQ-5.
/// </summary>
public sealed record CarouselBackgroundPlan(
    BackgroundSourcingPolicy Policy,
    IReadOnlyList<BackgroundSelection> PerSlide,
    string SeedToken)
{
    /// <summary>
    /// Returns the background selection for the given 1-based slide index.
    /// Clamps to the first entry when <paramref name="slideIndex"/> is less than 1,
    /// and to the last entry on overflow, so a plan is always readable without bounds checks.
    /// </summary>
    public BackgroundSelection ForSlide(int slideIndex)
    {
        if (PerSlide.Count == 0)
        {
            return new BackgroundSelection.Creative();
        }

        // Convert to 0-based, clamp to valid range
        var zeroIndex = Math.Max(0, slideIndex - 1);
        zeroIndex = Math.Min(zeroIndex, PerSlide.Count - 1);
        return PerSlide[zeroIndex];
    }

    /// <summary>
    /// Returns true when all per-slide selections share the same runtime type (coherent style family).
    /// A single-slide plan is always coherent.
    /// </summary>
    public bool IsCoherent
    {
        get
        {
            if (PerSlide.Count <= 1)
            {
                return true;
            }

            var firstType = PerSlide[0].GetType();
            return PerSlide.All(s => s.GetType() == firstType);
        }
    }

    /// <summary>
    /// Returns a new plan where each 1-based slide index present in <paramref name="overrides"/> is
    /// replaced with the supplied <see cref="BackgroundSelection.ForcedDefault"/>.
    /// Indices not present in the map are left unchanged.
    /// Out-of-range indices (less than 1 or greater than <see cref="PerSlide"/>.Count) are silently ignored.
    /// The original plan is NOT mutated; the method returns a new record.
    /// Refs: design §4.1; tasks WU-8.4.
    /// </summary>
    public CarouselBackgroundPlan WithForcedDefaults(
        IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault> overrides)
    {
        if (overrides.Count == 0)
        {
            return this;
        }

        var updated = new BackgroundSelection[PerSlide.Count];
        for (var i = 0; i < PerSlide.Count; i++)
        {
            var oneBasedIndex = i + 1;
            updated[i] = overrides.TryGetValue(oneBasedIndex, out var forced)
                ? forced
                : PerSlide[i];
        }

        return this with { PerSlide = updated };
    }

    /// <summary>
    /// Deterministic hash combining <paramref name="seed"/> and <paramref name="slideIndex"/>.
    /// Uses FNV-1a over the string "<paramref name="seed"/>:<paramref name="slideIndex"/>".
    /// Always returns a non-negative value suitable for use as a modulo index.
    /// Defined in Domain so the rotation algorithm is testable without infrastructure.
    /// </summary>
    public static int StableHash(string seed, int slideIndex)
    {
        // FNV-1a 32-bit over UTF-8 bytes of "{seed}:{slideIndex}"
        const uint FnvPrime = 16_777_619;
        const uint FnvOffsetBasis = 2_166_136_261;

        var text = $"{seed}:{slideIndex}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);

        uint hash = FnvOffsetBasis;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        // Return as non-negative int (mask sign bit)
        return (int)(hash & 0x7FFF_FFFF);
    }
}
