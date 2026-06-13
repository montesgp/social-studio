namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// The format of a content piece (canonical generation format key) plus its coarse
/// <see cref="ContentMedium"/> classification used for preview/edit dispatch.
/// </summary>
public sealed record ContentKind(
    string FormatKey,
    ContentMedium Medium);
