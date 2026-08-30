namespace Incoders.Hive.Domain.Social.ContentFormats;

/// <summary>
/// The top-level surface a content piece targets (the "root" content type).
/// Determines which layout options are applicable and how the piece is published.
/// </summary>
public enum Surface
{
    Script = 0,
    Story  = 1,
    Feed   = 2,
}
