namespace SocialStudio.Domain.Social.ProfileAnalysis;

/// <summary>
/// A durable post image reference paired with the caption of its parent post.
/// The structured input contract for aesthetic extraction (CACD-PA-03; OQ-3).
/// </summary>
/// <remarks>
/// <para>
/// Pairing is key-based on <see cref="ProfilePostImageRef.SourcePostId"/>: each archived slide image
/// carries the MESSAGE of the post it belonged to, enabling the multimodal extractor to learn how
/// the client translates a message into a visual concept.
/// </para>
/// <para>
/// <see cref="Caption"/> is null when the parent post has no caption (image-only scenario) or when
/// no matching post was found during the join. The extraction NEVER throws on a missing caption.
/// </para>
/// <para>Pure Domain value type — no Application or Infrastructure imports.</para>
/// </remarks>
/// <param name="Image">Durable reference to the archived slide image.</param>
/// <param name="Caption">
/// Caption of the parent post; null when the post has no caption or when the post is absent
/// from the caller's dataset (image-only fallback; CACD-PA-03-b/d).
/// </param>
public sealed record ProfilePostImageCaptionPair(ProfilePostImageRef Image, string? Caption);
