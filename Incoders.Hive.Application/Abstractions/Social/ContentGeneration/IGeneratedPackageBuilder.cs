using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for assembling rendered assets into a downloadable ZIP package.
/// Implemented in Application layer using BCL <c>System.IO.Compression.ZipArchive</c>.
/// </summary>
public interface IGeneratedPackageBuilder
{
    /// <summary>
    /// Builds a ZIP package from the supplied rendered assets.
    /// </summary>
    /// <exception cref="PackagingException">When <paramref name="input"/> has no assets.</exception>
    Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct);
}

/// <summary>Input model for the package builder.</summary>
public sealed record BuildPackageInput(
    IReadOnlyList<RenderedAsset> Assets,
    IReadOnlyList<string> InferredFields,
    IReadOnlyList<string> Warnings,
    Guid OrganizationId,
    Guid JobId,
    IReadOnlyDictionary<string, PublicationMetadata>? PublicationByFormat = null,
    /// <summary>
    /// Raw client name that will be normalized into a subfolder within the ZIP.
    /// When null or blank the builder falls back to a flat root layout (legacy behaviour).
    /// </summary>
    string? ClientName = null);

/// <summary>The result of a successful package build.</summary>
public sealed record BuiltPackage(
    Stream Content,
    long SizeBytes,
    GeneratedPackageManifest Manifest,
    string Filename);

/// <summary>Domain-level packaging error. Thrown (not returned) to simplify caller code.</summary>
public sealed class PackagingException : Exception
{
    public PackagingException(string message) : base(message) { }
}
