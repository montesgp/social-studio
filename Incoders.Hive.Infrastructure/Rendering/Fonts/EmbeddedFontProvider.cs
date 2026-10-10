using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;
using SkiaSharp;

namespace Incoders.Hive.Infrastructure.Rendering.Fonts;

/// <summary>
/// Reads fonts bundled as embedded resources in this assembly. Never depends on host-provisioned
/// fonts (Decision A1 — the render host's font state is unknown; there is no Dockerfile in this
/// repository provisioning fonts, and <c>SkiaSharp.NativeAssets.Linux.NoDependencies</c> gives no
/// fontconfig guarantee on Linux).
/// </summary>
public sealed class EmbeddedFontProvider : IFontProvider
{
    /// <summary>Emoji glyph used to probe color-emoji support: U+1F44B WAVING HAND SIGN.</summary>
    public const int ProbeCodepoint = 0x1F44B;

    private const string NotoColorEmojiFamily = "Noto Color Emoji";

    private const string AssetsNamespace = "Incoders.Hive.Infrastructure.Rendering.Fonts.Assets";

    // Every family FontCatalog.SystemSafeList offers MUST resolve here — the picker, the guardrail
    // allow-list, and this map are one contract (Decision F2). Real OFL substitute names, no
    // aliasing to Arial/Helvetica (Decision #1169).
    private static readonly IReadOnlyDictionary<string, string> ResourceNamesByFamily =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [NotoColorEmojiFamily] = $"{AssetsNamespace}.NotoColorEmoji.ttf",
            ["Liberation Sans"] = $"{AssetsNamespace}.LiberationSans-Regular.ttf",
            ["Liberation Serif"] = $"{AssetsNamespace}.LiberationSerif-Regular.ttf",
            ["Liberation Mono"] = $"{AssetsNamespace}.LiberationMono-Regular.ttf",
            ["DejaVu Sans"] = $"{AssetsNamespace}.DejaVuSans-Regular.ttf",
            ["DejaVu Serif"] = $"{AssetsNamespace}.DejaVuSerif-Regular.ttf",
        };

    /// <summary>
    /// Prefix of the curated client-typography catalog resources (client-style-fidelity D1). The
    /// csproj embeds them as <c>{prefix}{dir}/{file}</c>, where MSBuild's RecursiveDir may use either
    /// path separator depending on the build OS — <see cref="CatalogResourceNames"/> normalises that.
    /// </summary>
    private const string CatalogPrefix = $"{AssetsNamespace}.Catalog/";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> CatalogResourceNames = new(() =>
        typeof(EmbeddedFontProvider).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(CatalogPrefix, StringComparison.Ordinal))
            .ToDictionary(
                n => n[CatalogPrefix.Length..].Replace('\\', '/'),
                n => n,
                StringComparer.OrdinalIgnoreCase));

    private readonly Lazy<byte[]?> _emojiBytes = new(() =>
        ReadEmbeddedResource(ResourceNamesByFamily[NotoColorEmojiFamily]));

    private readonly Lazy<FontCapability> _capability;

    public EmbeddedFontProvider()
    {
        _capability = new Lazy<FontCapability>(ComputeCapability);
    }

    /// <inheritdoc />
    public byte[]? Resolve(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        if (ResourceNamesByFamily.TryGetValue(family, out var resourceName))
        {
            return ReadEmbeddedResource(resourceName);
        }

        // A catalog family's "default" file is the face closest to regular weight.
        return ResolveFaces(family)
            .OrderBy(f => Math.Abs(Math.Clamp(400, f.MinWeight, f.MaxWeight) - 400))
            .Select(f => f.Bytes)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public IReadOnlyList<EmbeddedFontFace> ResolveFaces(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return [];
        }

        if (ResourceNamesByFamily.TryGetValue(family, out var legacyResource))
        {
            var legacyBytes = ReadEmbeddedResource(legacyResource);
            return legacyBytes is { Length: > 0 } ? [new EmbeddedFontFace(legacyBytes, 400, 400)] : [];
        }

        var catalogFamily = BundledFontCatalog.Find(family);
        if (catalogFamily is null)
        {
            return [];
        }

        var faces = new List<EmbeddedFontFace>(catalogFamily.Faces.Count);
        foreach (var face in catalogFamily.Faces)
        {
            if (!CatalogResourceNames.Value.TryGetValue($"{catalogFamily.Directory}/{face.File}", out var name))
            {
                continue;
            }

            var bytes = ReadEmbeddedResource(name);
            if (bytes is { Length: > 0 })
            {
                faces.Add(new EmbeddedFontFace(bytes, face.MinWeight, face.MaxWeight));
            }
        }

        return faces;
    }

    /// <inheritdoc />
    public FontCapability Probe() => _capability.Value;

    private FontCapability ComputeCapability()
    {
        var bytes = _emojiBytes.Value;
        if (bytes is null || bytes.Length == 0)
        {
            return new FontCapability(SupportsColorEmoji: false);
        }

        using var typeface = SKTypeface.FromData(SKData.CreateCopy(bytes));
        if (typeface is null)
        {
            return new FontCapability(SupportsColorEmoji: false);
        }

        using var font = new SKFont(typeface);
        return new FontCapability(SupportsColorEmoji: font.ContainsGlyph(ProbeCodepoint));
    }

    private static byte[]? ReadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(EmbeddedFontProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
