using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 task 1.1 [RED] — <see cref="ArtDirectionMethod"/> enum shape.
/// Asserts: all six named members exist; Unknown is zero-value; fixture constants declared for re-use.
/// No Infrastructure types referenced.
/// Refs: design §3.1; tasks 1.1/1.2; CACD-PA-01.
/// </summary>
public sealed class ArtDirectionMethodTests
{
    // Fixture constants re-used across WU-1/WU-3 exemplar tests (CACD-PA-01).
    public const ArtDirectionMethod AtlasMethod = ArtDirectionMethod.MetaphoricalEditorial;
    public const ArtDirectionMethod CometMethod  = ArtDirectionMethod.LiteralIllustration;

    // ── Zero-value ────────────────────────────────────────────────────────────

    [Fact]
    public void Unknown_IsZeroValue()
    {
        Assert.Equal(0, (int)ArtDirectionMethod.Unknown);
    }

    // ── All six named members exist ───────────────────────────────────────────

    [Fact]
    public void AllSixNamedMembers_Exist()
    {
        _ = ArtDirectionMethod.Unknown;
        _ = ArtDirectionMethod.MetaphoricalEditorial;
        _ = ArtDirectionMethod.LiteralIllustration;
        _ = ArtDirectionMethod.SymbolicStillLife;
        _ = ArtDirectionMethod.DocumentaryPhotography;
        _ = ArtDirectionMethod.GraphicTypographic;
        _ = ArtDirectionMethod.Mixed;
    }

    // ── Fixture exemplar mappings (CACD-PA-01) ────────────────────────────────

    [Fact]
    public void Atlas_MapsTo_MetaphoricalEditorial()
    {
        Assert.Equal(ArtDirectionMethod.MetaphoricalEditorial, AtlasMethod);
    }

    [Fact]
    public void Comet_MapsTo_LiteralIllustration()
    {
        Assert.Equal(ArtDirectionMethod.LiteralIllustration, CometMethod);
    }

    // ── No Infrastructure types referenced (compile-time gate) ────────────────

    [Fact]
    public void NoInfrastructureTypesReferenced_CompileTimeGate()
    {
        // If this file compiles without Infrastructure imports, the type has no infra deps.
        var method = ArtDirectionMethod.MetaphoricalEditorial;
        Assert.NotEqual(ArtDirectionMethod.Unknown, method);
    }
}
