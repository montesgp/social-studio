namespace SocialStudio.Domain.Social.ContentGeneration;

/// <summary>
/// Summary manifest embedded in every generated package ZIP.
/// Lists formats produced, any inferred brand fields, warnings, and the normalized
/// client subfolder that the ZIP entries were placed under (e.g. <c>acme-corp</c>).
/// <para>
/// When <see cref="ClientFolderName"/> is present, format files live at
/// <c>{ClientFolderName}/{filename}</c> (carousel slides at
/// <c>{ClientFolderName}/carousel/{filename}</c>).
/// Legacy packages without this field use a flat root layout.
/// </para>
/// </summary>
public sealed record GeneratedPackageManifest(
    IReadOnlyDictionary<string, FormatAssetMetadata> Formats,
    IReadOnlyList<string> InferredFields,
    IReadOnlyList<string> Warnings,
    long TotalSizeBytes,
    string? ClientFolderName = null,
    /// <summary>
    /// Set to <c>true</c> when the job ran through the INC-3+ durable per-asset storage pipeline.
    /// Legacy frozen-ZIP packages leave this <c>false</c> (default).
    /// Frontend uses this to decide between assemble (new) and frozen-ZIP (legacy) download paths.
    /// </summary>
    bool HasStoredAssets = false);

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
