namespace Incoders.Hive.Domain.Social.ContentFormats;

/// <summary>
/// The rendering medium axis for a format. Distinct from
/// <see cref="ContentStudio.ContentMedium"/>, which is a coarse content-item classifier
/// used for preview and edit dispatch. This enum governs the format taxonomy axis
/// and determines whether a format produces visual assets (Image/Video) or text.
/// Medium.Video is representable in v1 even though no video format ships yet (REQ-10).
/// </summary>
public enum Medium
{
    Image = 0,
    Video = 1,
    Text  = 2,
}
