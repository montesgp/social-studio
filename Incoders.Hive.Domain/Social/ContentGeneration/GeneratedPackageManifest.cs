namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Summary manifest embedded in every generated package ZIP.
/// Lists formats produced, any inferred brand fields, and warnings.
/// </summary>
public sealed record GeneratedPackageManifest(
    IReadOnlyDictionary<string, FormatAssetMetadata> Formats,
    IReadOnlyList<string> InferredFields,
    IReadOnlyList<string> Warnings,
    long TotalSizeBytes);

/// <summary>Lightweight metadata for a single format asset inside the manifest.</summary>
public sealed record FormatAssetMetadata(
    string FormatKey,
    string Mime,
    string Filename,
    long SizeBytes,
    string ContentHash,
    PublicationMetadata? Publication = null);

/// <summary>Optional social publication metadata for a generated format asset.</summary>
public sealed record PublicationMetadata(
    string Description);
