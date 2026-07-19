using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;
using Incoders.Hive.Infrastructure.Rendering.Fonts;

namespace Incoders.Hive.Tests.Infrastructure.Rendering.Fonts;

/// <summary>
/// TDD tests for <see cref="EmbeddedFontProvider"/> — the Infrastructure adapter for
/// <see cref="IFontProvider"/> (Content Studio editing completeness, Phase 1 emoji render spike,
/// task 1.1).
/// </summary>
[Collection("SkiaRenderingSerial")]
public sealed class EmbeddedFontProviderTests
{
    [Fact]
    public void Resolve_NotoColorEmoji_ReturnsNonEmptyBytes()
    {
        IFontProvider provider = new EmbeddedFontProvider();

        var bytes = provider.Resolve("Noto Color Emoji");

        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 0, "Embedded emoji font bytes must be non-empty.");
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveOnFamilyName()
    {
        IFontProvider provider = new EmbeddedFontProvider();

        var bytes = provider.Resolve("noto color emoji");

        Assert.NotNull(bytes);
    }

    [Fact]
    public void Resolve_UnknownFamily_ReturnsNull()
    {
        IFontProvider provider = new EmbeddedFontProvider();

        var bytes = provider.Resolve("Definitely Not A Bundled Font");

        Assert.Null(bytes);
    }

    [Fact]
    public void Resolve_NullOrWhitespaceFamily_ReturnsNull()
    {
        IFontProvider provider = new EmbeddedFontProvider();

        Assert.Null(provider.Resolve(""));
        Assert.Null(provider.Resolve("   "));
    }

    [Fact]
    public void Probe_ReportsSupportsColorEmoji_ForKnownGlyph()
    {
        // U+1F44B WAVING HAND SIGN — the probe codepoint per Decision F3.
        IFontProvider provider = new EmbeddedFontProvider();

        var capability = provider.Probe();

        Assert.True(capability.SupportsColorEmoji, "Bundled Noto Color Emoji must contain U+1F44B.");
    }

    [Fact]
    public void Probe_IsStableAcrossCalls()
    {
        IFontProvider provider = new EmbeddedFontProvider();

        var first = provider.Probe();
        var second = provider.Probe();

        Assert.Equal(first, second);
    }

    // -----------------------------------------------------------------------
    // FontCatalog.SystemSafeList bundling (Content Studio editing completeness, Phase 2, Decision
    // F1/#1169) — every system-safe-list family must be resolvable through the SAME mechanism as the
    // emoji font, or the guardrail/picker contract (F2) is broken.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Liberation Sans")]
    [InlineData("Liberation Serif")]
    [InlineData("Liberation Mono")]
    [InlineData("DejaVu Sans")]
    [InlineData("DejaVu Serif")]
    public void Resolve_EverySystemSafeListFont_ReturnsNonEmptyBytes(string family)
    {
        IFontProvider provider = new EmbeddedFontProvider();

        var bytes = provider.Resolve(family);

        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 0, $"Embedded bytes for '{family}' must be non-empty.");
    }

    [Fact]
    public void FontCatalog_SystemSafeList_IsFullyResolvableByEmbeddedFontProvider()
    {
        // The contract (Decision F2): FontCatalog.SystemSafeList and IFontProvider must never
        // disagree about what is actually bundled.
        IFontProvider provider = new EmbeddedFontProvider();

        foreach (var family in FontCatalog.SystemSafeList)
        {
            Assert.NotNull(provider.Resolve(family));
        }
    }
}
