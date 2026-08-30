namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// Coarse classification of a content piece, used to dispatch preview and edit behavior
/// without branching on raw format-key strings.
/// </summary>
public enum ContentMedium
{
    Text = 0,
    Visual = 1,
    Video = 2,
    Document = 3,
}
