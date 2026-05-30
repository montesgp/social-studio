using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Packaging;

/// <summary>
/// Assembles a ZIP archive from rendered assets.
/// Uses BCL <see cref="ZipArchive"/> — no external NuGet dependency.
/// <para>
/// Carousel slides are placed under a <c>carousel/</c> folder.
/// All other formats are placed at the ZIP root.
/// A <c>manifest.json</c> entry is always written at the root.
/// </para>
/// </summary>
public sealed class GeneratedPackageBuilder : IGeneratedPackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        if (input.Assets.Count == 0)
        {
            throw new PackagingException("Cannot build a package with no assets.");
        }

        var ms = new MemoryStream();
        var formatMeta = new Dictionary<string, FormatAssetMetadata>();
        long totalSize = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var asset in input.Assets)
            {
                // Carousel slides go under a sub-folder
                var entryPath = asset.FormatKey == "carousel"
                    ? $"carousel/{asset.Filename}"
                    : asset.Filename;

                var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                var bytes = asset.GetBytes();
                entryStream.Write(bytes, 0, bytes.Length);
                totalSize += bytes.Length;

                // Last asset metadata wins per format key (for multi-slide carousel, last slide)
                PublicationMetadata? publication = null;
                input.PublicationByFormat?.TryGetValue(asset.FormatKey, out publication);
                formatMeta[asset.FormatKey] = new FormatAssetMetadata(
                    asset.FormatKey,
                    asset.Mime,
                    asset.Filename,
                    asset.SizeBytes,
                    asset.ContentHash,
                    publication);
            }

            // Write manifest.json
            var manifest = new GeneratedPackageManifest(
                Formats: formatMeta,
                InferredFields: input.InferredFields.ToList(),
                Warnings: input.Warnings.ToList(),
                TotalSizeBytes: totalSize);

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var manifestStream = manifestEntry.Open();
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            manifestStream.Write(manifestBytes, 0, manifestBytes.Length);

            var filename = $"{input.JobId:N}.zip";

            // Return the completed package
            var built = new BuiltPackage(ms, totalSize, manifest, filename);
            return Task.FromResult(built);
        }
    }
}
