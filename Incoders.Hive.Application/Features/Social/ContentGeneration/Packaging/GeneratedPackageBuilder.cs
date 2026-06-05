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

    // Strip the 9 cross-OS-illegal characters (< > : " / \ | ? *), DEL (0x7F), and C0 control chars (0x00-0x1F).
    // Covers: Linux '/', macOS ':', Windows full reserved set.
    // Preserve case, accents, internal spaces, emoji, ampersand, dots, and all other characters.
    private static readonly Regex ReservedAndControl =
        new(@"[<>:""/\\|?*\x00-\x1F\x7F]", RegexOptions.Compiled);

    // Windows reserved device names — full match only, case-insensitive.
    // COM1-COM9 and LPT1-LPT9 (COM10+ are NOT reserved).
    private static readonly Regex DeviceName =
        new(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9]|CLOCK\$)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Cross-OS folder name sanitizer. Strips filesystem-illegal characters and guards
    /// against OS-reserved names, while preserving case, accents, internal spaces, and emoji.
    /// Rules applied in order:
    ///   1. Strip reserved chars (the 9 Windows-illegal + DEL + C0 control chars).
    ///   2. TrimStart('.', ' ') — leading dots (Unix hidden-file) and leading spaces.
    ///   3. TrimEnd('.', ' ') — trailing dots/spaces (Windows disallows these as leaf names).
    ///   4. If the whole result is a Windows reserved device name (CON, NUL, COM1…), append '_'.
    /// Returns empty string when the name is null/whitespace or all chars were stripped.
    /// </summary>
    internal static string NormalizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var sanitized = ReservedAndControl
            .Replace(name, string.Empty)
            .TrimStart('.', ' ')
            .TrimEnd('.', ' ');

        if (sanitized.Length == 0) return string.Empty;

        // Guard Windows reserved device names — whole-name match only.
        if (DeviceName.IsMatch(sanitized))
            sanitized += "_";

        return sanitized;
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
        var formatAssetIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long totalSize = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var asset in input.Assets)
            {
                var archiveFilename = ResolveArchiveFilename(asset, formatAssetIndexes);
                string entryPath;
                if (hasClientFolder)
                {
                    entryPath = asset.FormatKey == "carousel"
                        ? $"{clientFolder}/carousel/{archiveFilename}"
                        : $"{clientFolder}/{archiveFilename}";
                }
                else
                {
                    // Legacy flat layout
                    entryPath = asset.FormatKey == "carousel"
                        ? $"carousel/{archiveFilename}"
                        : archiveFilename;
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
                    asset.FormatKey == "carousel" ? "carousel/" : archiveFilename,
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

    private static string ResolveArchiveFilename(
        RenderedAsset asset,
        IDictionary<string, int> formatAssetIndexes)
    {
        var sourceFilename = FileNameOnly(asset.Filename);
        if (!string.Equals(asset.FormatKey, "carousel", StringComparison.OrdinalIgnoreCase))
        {
            return sourceFilename;
        }

        var nextIndex = formatAssetIndexes.TryGetValue(asset.FormatKey, out var current)
            ? current + 1
            : 1;
        formatAssetIndexes[asset.FormatKey] = nextIndex;

        var extension = Path.GetExtension(sourceFilename);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        return $"slide_{nextIndex}{extension}";
    }

    private static string FileNameOnly(string filename)
    {
        var normalized = filename.Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
    }
}

