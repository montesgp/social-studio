namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// One embedded font file and the weight range it covers (a variable font spans a range; a static
/// file has <see cref="MinWeight"/> equal to <see cref="MaxWeight"/>).
/// </summary>
public sealed record EmbeddedFontFace(byte[] Bytes, int MinWeight, int MaxWeight);
