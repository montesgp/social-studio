using Incoders.Hive.ApiService.Endpoints.Social;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;

namespace Incoders.Hive.Tests.ApiService;

/// <summary>
/// client-style-fidelity CSF-CS-01: Content Studio loads the server renderer's own font files, so
/// the stylesheet must declare every bundled face with its real weight range.
/// </summary>
public sealed class ContentStudioFontEndpointsTests
{
    [Fact]
    public void Stylesheet_DeclaresEveryCatalogFaceWithItsWeightRange()
    {
        var css = ContentStudioFontEndpointsExtensions.BuildStylesheet();

        foreach (var family in BundledFontCatalog.Families)
        {
            for (var i = 0; i < family.Faces.Count; i++)
            {
                var face = family.Faces[i];
                Assert.Contains(
                    $"@font-face{{font-family:'{family.Name}';src:url('fonts/{family.Directory}/{i}') format('truetype');font-weight:{face.MinWeight} {face.MaxWeight};",
                    css,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Stylesheet_UsesRelativeUrls_SoItWorksBehindTheWebProxy()
    {
        var css = ContentStudioFontEndpointsExtensions.BuildStylesheet();

        Assert.DoesNotContain("url('/", css, StringComparison.Ordinal);
        Assert.DoesNotContain("http", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderCapabilities_MarkCatalogFamiliesAsBundled()
    {
        var dto = FontCatalog.For([]).Select(FontEntryDto.FromDomain).ToList();

        Assert.Contains(dto, f => f.Family == "League Gothic" && f.IsBundled);
        Assert.Contains(dto, f => f.Family == "Liberation Sans" && f.IsBundled);
    }
}
