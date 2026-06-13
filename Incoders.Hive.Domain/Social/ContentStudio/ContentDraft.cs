namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// The editable working payload of a <see cref="ContentItem"/>. Initialized from — but decoupled
/// from — the immutable source format result at materialization; thereafter owned and mutated by the
/// item. The source generation output is never written back.
/// </summary>
public sealed record ContentDraft(
    string? Text,
    string? VisualSpecMarkup,
    IReadOnlyList<ContentAssetRef> Assets,
    TargetChannel TargetChannel)
{
    /// <summary>Creates a text-only draft (e.g. caption / script) for the given channel.</summary>
    public static ContentDraft ForText(string? text, TargetChannel targetChannel) =>
        new(text, null, Array.Empty<ContentAssetRef>(), targetChannel);
}
