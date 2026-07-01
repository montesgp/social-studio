using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 task 1.3 [RED] — <see cref="ConceptualMethod"/> record shape.
/// Asserts: construction compiles; Cues is immutable IReadOnlyList; with-expression compiles;
/// no Infrastructure types referenced.
/// Refs: design §3.1; tasks 1.3/1.4; CACD-PA-01.
/// </summary>
public sealed class ConceptualMethodTests
{
    // ── Construction ──────────────────────────────────────────────────────────

    [Fact]
    public void Construction_AllParams_CompilesAndHoldsValues()
    {
        IReadOnlyList<string> cues = ["mono-color saturated backdrop", "object-as-metaphor"];
        var method = new ConceptualMethod(
            ArtDirectionMethod.MetaphoricalEditorial,
            "Message becomes a visual metaphor",
            cues);

        Assert.Equal(ArtDirectionMethod.MetaphoricalEditorial, method.Method);
        Assert.Equal("Message becomes a visual metaphor", method.Rationale);
        Assert.Equal(2, method.Cues.Count);
        Assert.Equal("mono-color saturated backdrop", method.Cues[0]);
        Assert.Equal("object-as-metaphor", method.Cues[1]);
    }

    // ── Cues is IReadOnlyList (immutable interface) ───────────────────────────

    [Fact]
    public void Cues_IsIReadOnlyList_NotMutable()
    {
        IReadOnlyList<string> cues = ["staged human subject"];
        var method = new ConceptualMethod(ArtDirectionMethod.LiteralIllustration, "Tender literal scene", cues);

        Assert.IsAssignableFrom<IReadOnlyList<string>>(method.Cues);
    }

    // ── with-expression compiles ──────────────────────────────────────────────

    [Fact]
    public void WithExpression_Method_CompilesAndYieldsNewValue()
    {
        IReadOnlyList<string> cues = ["tender watercolor", "children characters"];
        var original = new ConceptualMethod(ArtDirectionMethod.LiteralIllustration, "Comet style", cues);
        var updated  = original with { Method = ArtDirectionMethod.Mixed };

        Assert.Equal(ArtDirectionMethod.Mixed, updated.Method);
        Assert.Equal(ArtDirectionMethod.LiteralIllustration, original.Method);
    }

    [Fact]
    public void WithExpression_Rationale_CompilesAndYieldsNewValue()
    {
        IReadOnlyList<string> cues = ["mono-color backdrop"];
        var original = new ConceptualMethod(ArtDirectionMethod.MetaphoricalEditorial, "original", cues);
        var updated  = original with { Rationale = "updated rationale" };

        Assert.Equal("updated rationale", updated.Rationale);
        Assert.Equal("original", original.Rationale);
    }

    // ── Value equality ────────────────────────────────────────────────────────

    [Fact]
    public void EqualityIsByValue()
    {
        IReadOnlyList<string> cues = ["mono-color backdrop"];
        var a = new ConceptualMethod(ArtDirectionMethod.MetaphoricalEditorial, "same", cues);
        var b = new ConceptualMethod(ArtDirectionMethod.MetaphoricalEditorial, "same", cues);

        Assert.Equal(a, b);
    }

    // ── No Infrastructure types (compile-time gate) ───────────────────────────

    [Fact]
    public void NoInfrastructureTypesReferenced_CompileTimeGate()
    {
        var method = new ConceptualMethod(ArtDirectionMethod.GraphicTypographic, "Bold graphic", []);
        Assert.Equal(ArtDirectionMethod.GraphicTypographic, method.Method);
    }
}
