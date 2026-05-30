using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Packaging;

/// <summary>
/// Assembles a ZIP archive from rendered assets.
/// Uses BCL <see cref="ZipArchive"/> — no external NuGet dependency.
/// <para>
/// When a client name is supplied via <see cref="BuildPackageInput.ClientName"/>, all format
/// files are placed under a normalized <c>{client-folder}/</c> subfolder so that packages for
/// different clients remain clearly separated.  Carousel slides are nested one level deeper:
/// <c>{client-folder}/carousel/{slide}</c>.  A <c>manifest.json</c> is always written at the
/// ZIP root.
/// </para>
/// <para>
/// Legacy packages (no <see cref="BuildPackageInput.ClientName"/>) keep the previous flat
/// layout: <c>carousel/{slide}</c> for carousel slides, file-at-root for everything else.
/// </para>
/// </summary>
public sealed class GeneratedPackageBuilder : IGeneratedPackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Normalise a client display name into a safe folder slug: lowercase, collapse
    // non-alphanumeric runs to a single dash, trim leading/trailing dashes.
    private static readonly Regex SlugUnsafe = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    internal static string NormalizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var slug = SlugUnsafe.Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug;
    }

    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        if (input.Assets.Count == 0)
        {
            throw new PackagingException("Cannot build a package with no assets.");
        }

        var clientFolder = NormalizeFolderName(input.ClientName);
        var hasClientFolder = !string.IsNullOrEmpty(clientFolder);

        var ms = new MemoryStream();
        var formatMeta = new Dictionary<string, FormatAssetMetadata>();
        long totalSize = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var asset in input.Assets)
            {
                string entryPath;
                if (hasClientFolder)
                {
                    entryPath = asset.FormatKey == "carousel"
                        ? $"{clientFolder}/carousel/{asset.Filename}"
                        : $"{clientFolder}/{asset.Filename}";
                }
                else
                {
                    // Legacy flat layout
                    entryPath = asset.FormatKey == "carousel"
                        ? $"carousel/{asset.Filename}"
                        : asset.Filename;
                }

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

            // Write manifest.json at the ZIP root
            var manifest = new GeneratedPackageManifest(
                Formats: formatMeta,
                InferredFields: input.InferredFields.ToList(),
                Warnings: input.Warnings.ToList(),
                TotalSizeBytes: totalSize,
                ClientFolderName: hasClientFolder ? clientFolder : null);

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var manifestStream = manifestEntry.Open();
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            manifestStream.Write(manifestBytes, 0, manifestBytes.Length);

            var filename = $"{input.JobId:N}.zip";

            var built = new BuiltPackage(ms, totalSize, manifest, filename);
            return Task.FromResult(built);
        }
    }
}

