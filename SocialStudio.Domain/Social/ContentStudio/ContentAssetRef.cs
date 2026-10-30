namespace SocialStudio.Domain.Social.ContentStudio;

/// <summary>
/// A soft reference to a stored asset that backs a content draft (background, image, rendered output).
/// Holds only a storage URI and metadata — never a binary payload. Storage is owned by Infrastructure ports.
/// </summary>
public sealed record ContentAssetRef(
    string Role,
    string StorageUri,
    string Mime);
