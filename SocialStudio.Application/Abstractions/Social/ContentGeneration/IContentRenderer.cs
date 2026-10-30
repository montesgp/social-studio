using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for rendering a <see cref="VisualCompositionSpec"/> into binary assets (PNG v1).
/// Infrastructure implementations: <c>LocalSvgContentRenderer</c> (v1).
/// Future adapters: Canva, Stability, Imagen.
/// </summary>
public interface IContentRenderer
{
    /// <summary>
    /// Renders the spec into one or more <see cref="RenderedAsset"/>s.
    /// Returns a failure result instead of throwing on render errors.
    /// </summary>
    Task<Result<RenderedAsset>> RenderAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct);
}

/// <summary>Options that control rendering behaviour (v1: minimal).</summary>
public sealed record RenderOptions
{
    /// <summary>Target MIME type. Defaults to <c>image/png</c>.</summary>
    public string Mime { get; init; } = "image/png";
}

/// <summary>Typed render error. Returned inside a failed <see cref="Result{RenderedAsset}"/>.</summary>
public static class RenderErrors
{
    public static readonly Error InvalidSpec =
        Error.Failure("render.invalid-spec", "render.invalidSpec");

    public static readonly Error RenderFailure =
        Error.Failure("render.failure", "render.failure");

    public static Error RenderFailed(string detail) =>
        Error.Failure("render.failure", detail);
}
