using System.Globalization;
using System.Text;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

/// <summary>
/// Serves the bundled client-typography catalog to Content Studio (client-style-fidelity CSF-CS-01),
/// so the editor draws slide text with the same faces the server-side render embeds. Anonymous on
/// purpose: the files are OFL-licensed and a CSS <c>&lt;link&gt;</c> cannot send a bearer token.
/// </summary>
public static class ContentStudioFontEndpointsExtensions
{
    public const string StylesheetPath = "/api/social/content-studio/fonts.css";
    private const string FontFilePathPrefix = "/api/social/content-studio/fonts";

    private static readonly Lazy<string> Stylesheet = new(BuildStylesheet);

    public static IEndpointRouteBuilder MapContentStudioFontEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(StylesheetPath, GetStylesheet)
            .AllowAnonymous()
            .WithTags("Social ContentStudio")
            .WithName("GetContentStudioFontStylesheet")
            .WithSummary("@font-face rules for every bundled client-typography family")
            .Produces(StatusCodes.Status200OK);

        app.MapGet($"{FontFilePathPrefix}/{{directory}}/{{faceIndex:int}}", GetFontFile)
            .AllowAnonymous()
            .WithTags("Social ContentStudio")
            .WithName("GetContentStudioFontFile")
            .WithSummary("One embedded font file from the bundled client-typography catalog")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult GetStylesheet(HttpContext http)
    {
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Text(Stylesheet.Value, "text/css", Encoding.UTF8);
    }

    private static IResult GetFontFile(string directory, int faceIndex, IFontProvider fonts, HttpContext http)
    {
        var family = BundledFontCatalog.Families.FirstOrDefault(f =>
            string.Equals(f.Directory, directory, StringComparison.OrdinalIgnoreCase));
        if (family is null)
        {
            return Results.NotFound();
        }

        var faces = fonts.ResolveFaces(family.Name);
        if (faceIndex < 0 || faceIndex >= faces.Count)
        {
            return Results.NotFound();
        }

        http.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Bytes(faces[faceIndex].Bytes, "font/ttf");
    }

    /// <summary>
    /// Face URLs are relative to the stylesheet, so they resolve against whatever origin served it.
    /// Face indexes follow <see cref="BundledFontFamily.Faces"/> order, the same order
    /// <see cref="IFontProvider.ResolveFaces"/> returns.
    /// </summary>
    internal static string BuildStylesheet()
    {
        var css = new StringBuilder();
        foreach (var family in BundledFontCatalog.Families)
        {
            for (var i = 0; i < family.Faces.Count; i++)
            {
                var face = family.Faces[i];
                css.Append(CultureInfo.InvariantCulture,
                    $"@font-face{{font-family:'{family.Name}';src:url('fonts/{family.Directory}/{i}') format('truetype');font-weight:{face.MinWeight} {face.MaxWeight};font-display:swap;}}\n");
            }
        }

        return css.ToString();
    }
}
