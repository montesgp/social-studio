namespace SocialStudio.Domain.Social.ContentFormats;

/// <summary>
/// The layout variant of a content piece. Null when the Surface is Script
/// (scripts have no applicable layout axis).
/// </summary>
public enum Layout
{
    Single   = 0,
    Carousel = 1,
}
