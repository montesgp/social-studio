using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// TDD test for task 2.9 (Content Studio editing completeness, Decision #1169): the bundled
/// typography fallback must NOT be "Arial" — Arial is Monotype-licensed and cannot be legally
/// embedded, nor guaranteed present on the render host. It must resolve to a catalog-bundled OFL
/// substitute instead.
/// </summary>
public sealed class CompositionDefaultsTypographyTests
{
    [Fact]
    public void Typography_FallbackFamily_IsNotArial()
    {
        Assert.NotEqual("Arial", CompositionDefaults.Typography.HeadingFamily);
        Assert.NotEqual("Arial", CompositionDefaults.Typography.BodyFamily);
    }

    [Fact]
    public void Typography_FallbackFamily_ResolvesToACatalogBundledEntry()
    {
        var catalog = FontCatalog.For(Array.Empty<string>());
        var families = catalog.Select(e => e.Family).ToArray();

        Assert.Contains(CompositionDefaults.Typography.HeadingFamily, families);
        Assert.Contains(CompositionDefaults.Typography.BodyFamily, families);

        var heading = catalog.Single(e => e.Family == CompositionDefaults.Typography.HeadingFamily);
        Assert.True(heading.IsBundled, "The fallback family must be a bundled (embeddable) catalog entry.");
    }
}
