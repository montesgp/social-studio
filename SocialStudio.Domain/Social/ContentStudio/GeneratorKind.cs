namespace SocialStudio.Domain.Social.ContentStudio;

/// <summary>
/// Identifies which generator produced the content a <see cref="ContentItem"/> is derived from.
/// Open enumeration: new generators are added here without changing the ContentStudio aggregates.
/// </summary>
public enum GeneratorKind
{
    SingleAnalyzer = 0,
    MagicGenerator = 1,
}
