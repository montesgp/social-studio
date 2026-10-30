namespace SocialStudio.Domain.Social.ContentStudio;

/// <summary>
/// The editable working payload of a <see cref="ContentItem"/>. Initialized from — but decoupled
/// from — the immutable source format result at materialization; thereafter owned and mutated by the
/// item. The source generation output is never written back.
/// </summary>
/// <param name="Text">
/// Reel scripts and other non-caption textual content. Unconstrained (may be null). This is a SPLIT
/// from <see cref="Caption"/> (Decision E2 — Content Studio editing completeness), not a rename:
/// <c>Text</c> keeps carrying scripts exactly as before.
/// </param>
/// <param name="Caption">
/// The dedicated, IG-bounded publication caption (see <see cref="Social.ContentStudio.Caption"/>).
/// Nullable on the record — a required non-nullable field would make every legacy row
/// unconstructible — but non-empty and bounded WHEN present, per the value object's own invariant.
/// </param>
public sealed record ContentDraft(
    string? Text,
    Caption? Caption,
    string? VisualSpecMarkup,
    IReadOnlyList<ContentAssetRef> Assets,
    TargetChannel TargetChannel)
{
    /// <summary>Creates a text-only draft (e.g. a reel script) for the given channel.</summary>
    public static ContentDraft ForText(string? text, TargetChannel targetChannel) =>
        new(text, null, null, Array.Empty<ContentAssetRef>(), targetChannel);

    /// <summary>Creates a caption-only draft for the given channel.</summary>
    public static ContentDraft ForCaption(Caption caption, TargetChannel targetChannel) =>
        new(null, caption, null, Array.Empty<ContentAssetRef>(), targetChannel);
}
