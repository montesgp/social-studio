using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Carousel;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Formats;

/// <summary>
/// WU-2 RED tests: CarouselGenerator effective slide-count resolution.
/// Refs: CAC-CG-01; design §0 OQ-3; tasks 2.3.
/// </summary>
public sealed class CarouselGeneratorSlideCountTests
{
    // -----------------------------------------------------------------------
    // CAC-CG-01-a variant: explicit user-requested count wins over profile
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveEffectiveSlideCount_UserRequestedWins_WhenProfileHasTypicalCount()
    {
        // User asked for 4 slides; profile says 7 → user wins.
        var result = CarouselGenerator.ResolveEffectiveSlideCount(userRequested: 4, typicalSlideCount: 7);

        Assert.Equal(4, result);
    }

    // -----------------------------------------------------------------------
    // CAC-CG-01-a: no user request → profile TypicalSlideCount used
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveEffectiveSlideCount_UsesTypicalCount_WhenNoUserRequest()
    {
        // No explicit request; profile typical = 7 (within [3..10]) → effective = 7.
        var result = CarouselGenerator.ResolveEffectiveSlideCount(userRequested: null, typicalSlideCount: 7);

        Assert.Equal(7, result);
    }

    // -----------------------------------------------------------------------
    // OQ-3 clamp: TypicalSlideCount > 10 → clamped to 10
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveEffectiveSlideCount_ClampsHighTypicalCount()
    {
        // Pathological captured value 15 → clamped to max 10.
        var result = CarouselGenerator.ResolveEffectiveSlideCount(userRequested: null, typicalSlideCount: 15);

        Assert.Equal(10, result);
    }

    // -----------------------------------------------------------------------
    // OQ-3 clamp: TypicalSlideCount < 3 → clamped to 3
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveEffectiveSlideCount_ClampsLowTypicalCount()
    {
        // Pathological captured value 1 → clamped to min 3.
        var result = CarouselGenerator.ResolveEffectiveSlideCount(userRequested: null, typicalSlideCount: 1);

        Assert.Equal(3, result);
    }

    // -----------------------------------------------------------------------
    // CAC-CG-01-b: no user request AND no profile → null (current default range)
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveEffectiveSlideCount_ReturnsNull_WhenBothInputsAreNull()
    {
        // Cold-start / no profile → null → caller uses existing default ("~3-5 slides").
        var result = CarouselGenerator.ResolveEffectiveSlideCount(userRequested: null, typicalSlideCount: null);

        Assert.Null(result);
    }
}
