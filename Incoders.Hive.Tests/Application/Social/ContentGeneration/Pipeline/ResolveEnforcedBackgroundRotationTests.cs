using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// WU-4 C6 TDD tests — ResolveEnforcedBackground behavior with per-slide rotated selections.
///
/// Pins that:
/// 1. For a per-slide References([rotatedSingle]) plan entry, the enforced background template
///    IS the slide's rotated entry — not always Entries[0] from a shared multi-entry set.
///    (CASE A puts a single-entry References per slide into the plan; first-entry-wins
///    correctly enforces the slide's rotated template, NOT a shared first entry.)
/// 2. ClientAesthetic selection → ResolveEnforcedBackground returns null (nothing to enforce;
///    the AI generates freely within the aesthetic guidance).
/// 3. Creative selection → null (no template enforcement).
/// 4. ForcedDefault → still enforced as before (regression guard).
///
/// These tests use a test-only reflection accessor for the private static method, or
/// call through the processor's test-accessible entry point. Since the method is private
/// static, we exercise it indirectly via a thin InternalsVisibleTo wrapper or by
/// running the full processor pipeline. To keep tests focused and fast, we use the
/// existing public interface and verify outcomes via the composition results.
///
/// Refs: design §6.1 C6; tasks 4.9/4.10.
/// </summary>
public sealed class ResolveEnforcedBackgroundRotationTests
{
    // -----------------------------------------------------------------------
    // C6 contract: ResolveEnforcedBackground is a private static method.
    // We test its behavior indirectly through the InternalsVisible mechanism
    // or through an exposed test accessor.
    // Since the method is private, we verify through the processor's pipeline
    // or via the ResolveEnforcedBackgroundAccessor helper below.
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN a CASE-A plan with 3 backgrounds rotated across 3 slides,
    /// WHEN the per-slide References([rotated]) selection enters ResolveEnforcedBackground,
    /// THEN the enforced template URL is the slide's specific rotated entry — NOT bg1 for every slide.
    /// This guards that first-entry-wins works on a per-slide single-entry References, not a global first.
    /// Refs: design §6.1 C6, §7.1.
    /// </summary>
    [Fact]
    public void ResolveEnforcedBackground_SingleEntryReferences_ReturnsTheSlideSpecificEntry()
    {
        var bg1 = new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative);
        var bg2 = new BrandBackgroundTemplateSnapshot("bg2", "https://cdn/bg2.jpg", null, BackgroundTier.Creative);
        var bg3 = new BrandBackgroundTemplateSnapshot("bg3", "https://cdn/bg3.jpg", null, BackgroundTier.Creative);

        // Simulate what ClientDefinedSetStrategy emits per slide: each gets exactly one entry.
        // This is what the plan looks like for CASE A with N>1 backgrounds.
        var slideSelections = new[]
        {
            new BackgroundSelection.References(new[] { bg2 }), // slide 1 got bg2 via stableHash
            new BackgroundSelection.References(new[] { bg3 }), // slide 2 got bg3
            new BackgroundSelection.References(new[] { bg1 }), // slide 3 got bg1
        };

        // Assert: each single-entry References enforces that slide's rotated template
        var results = slideSelections.Select(ResolveEnforcedBackgroundAccessor.Invoke).ToList();

        Assert.NotNull(results[0]);
        Assert.Equal("https://cdn/bg2.jpg", results[0]!.Url);
        Assert.NotNull(results[1]);
        Assert.Equal("https://cdn/bg3.jpg", results[1]!.Url);
        Assert.NotNull(results[2]);
        Assert.Equal("https://cdn/bg1.jpg", results[2]!.Url);
    }

    /// <summary>
    /// GIVEN a ClientAesthetic selection (CASE C),
    /// WHEN it enters ResolveEnforcedBackground,
    /// THEN null is returned — ClientAesthetic is creative-with-guidance, nothing to enforce.
    /// The AI markup path is taken for CASE C slides.
    /// Refs: design §7.1; tasks 4.10.
    /// </summary>
    [Fact]
    public void ResolveEnforcedBackground_ClientAesthetic_ReturnsNull()
    {
        var selection = new BackgroundSelection.ClientAesthetic(new[] { "minimalist", "clean white space" });

        var result = ResolveEnforcedBackgroundAccessor.Invoke(selection);

        Assert.Null(result);
    }

    /// <summary>
    /// GIVEN a Creative selection (CASE B — AI generates freely),
    /// WHEN it enters ResolveEnforcedBackground,
    /// THEN null is returned — no template to enforce.
    /// </summary>
    [Fact]
    public void ResolveEnforcedBackground_Creative_ReturnsNull()
    {
        var selection = new BackgroundSelection.Creative();

        var result = ResolveEnforcedBackgroundAccessor.Invoke(selection);

        Assert.Null(result);
    }

    /// <summary>
    /// GIVEN a ForcedDefault selection,
    /// WHEN it enters ResolveEnforcedBackground,
    /// THEN the enforced template is returned (regression guard — behavior unchanged from pre-WU-4).
    /// </summary>
    [Fact]
    public void ResolveEnforcedBackground_ForcedDefault_ReturnsEnforcedTemplate()
    {
        var selection = new BackgroundSelection.ForcedDefault("https://cdn/forced.jpg", "Forced BG");

        var result = ResolveEnforcedBackgroundAccessor.Invoke(selection);

        Assert.NotNull(result);
        Assert.Equal("https://cdn/forced.jpg", result!.Url);
        Assert.Equal("brand-manual-forced-default", result.Source);
    }

    /// <summary>
    /// GIVEN a multi-entry References (legacy selection with many templates),
    /// WHEN first-entry-wins is applied,
    /// THEN the FIRST entry with a non-empty URL is enforced.
    /// Regression guard — legacy behavior is preserved.
    /// </summary>
    [Fact]
    public void ResolveEnforcedBackground_MultiEntryReferences_ReturnsFirstEntry()
    {
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg2", "https://cdn/bg2.jpg", null, BackgroundTier.Creative),
        };
        var selection = new BackgroundSelection.References(templates);

        var result = ResolveEnforcedBackgroundAccessor.Invoke(selection);

        Assert.NotNull(result);
        Assert.Equal("https://cdn/bg1.jpg", result!.Url); // first-entry-wins
    }

    /// <summary>
    /// GIVEN a References selection with all entries having empty URLs (edge case),
    /// WHEN ResolveEnforcedBackground is called,
    /// THEN null is returned (no enforceable URL found).
    /// </summary>
    [Fact]
    public void ResolveEnforcedBackground_ReferencesWithNoValidUrl_ReturnsNull()
    {
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg2", "   ", null, BackgroundTier.Creative),
        };
        var selection = new BackgroundSelection.References(templates);

        var result = ResolveEnforcedBackgroundAccessor.Invoke(selection);

        Assert.Null(result);
    }
}

/// <summary>
/// Result wrapper for <c>ResolveEnforcedBackground</c> invocations via reflection.
/// Maps the private <c>EnforcedBackgroundTemplate</c> record to publicly accessible properties.
/// </summary>
file sealed class EnforcedBackgroundResult(string url, string name, string source)
{
    public string Url { get; } = url;
    public string Name { get; } = name;
    public string Source { get; } = source;
}

/// <summary>
/// Test accessor for <c>ContentGenerationJobProcessor.ResolveEnforcedBackground</c>.
/// Uses reflection to call the private static method so the production class stays un-modified
/// and the test pins the exact behavioral contract (not a copy of the logic).
/// </summary>
file static class ResolveEnforcedBackgroundAccessor
{
    private static readonly System.Reflection.MethodInfo Method;

    static ResolveEnforcedBackgroundAccessor()
    {
        Method = typeof(ContentGenerationJobProcessor)
            .GetMethod(
                "ResolveEnforcedBackground",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "ContentGenerationJobProcessor.ResolveEnforcedBackground not found. " +
                "If the method was renamed or removed, update this accessor.");
    }

    /// <summary>
    /// Invokes <c>ResolveEnforcedBackground(BackgroundSelection)</c> and returns the result
    /// mapped to an accessible wrapper (the actual return type is a private nested record).
    /// </summary>
    public static EnforcedBackgroundResult? Invoke(BackgroundSelection selection)
    {
        var result = Method.Invoke(null, [selection]);
        if (result is null)
        {
            return null;
        }

        // Map via reflection since EnforcedBackgroundTemplate is private
        var type = result.GetType();
        var url = (string?)type.GetProperty("Url")?.GetValue(result) ?? string.Empty;
        var name = (string?)type.GetProperty("Name")?.GetValue(result) ?? string.Empty;
        var source = (string?)type.GetProperty("Source")?.GetValue(result) ?? string.Empty;

        return new EnforcedBackgroundResult(url, name, source);
    }
}
