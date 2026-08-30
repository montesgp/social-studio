using System.Security.Cryptography;

namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// A rendered binary asset produced by <c>IContentRenderer</c> for a single format (or slide).
/// Immutable after construction; content hash is computed at creation time.
/// </summary>
public sealed record RenderedAsset
{
    private readonly byte[] _bytes;

    private RenderedAsset(string formatKey, string mime, string filename, byte[] bytes, string contentHash)
    {
        FormatKey = formatKey;
        Mime = mime;
        Filename = filename;
        SizeBytes = bytes.Length;
        ContentHash = contentHash;
        _bytes = bytes;
    }

    public string FormatKey { get; }

    public string Mime { get; }

    public string Filename { get; }

    public long SizeBytes { get; }

    /// <summary>SHA-256 hex string of the rendered bytes (lowercase).</summary>
    public string ContentHash { get; }

    /// <summary>Returns the raw rendered bytes.</summary>
    public byte[] GetBytes() => _bytes;

    /// <summary>
    /// Creates a <see cref="RenderedAsset"/> and computes the content hash.
    /// </summary>
    /// <exception cref="ArgumentException">When any string arg is empty or bytes are empty.</exception>
    public static RenderedAsset Create(string formatKey, string mime, string filename, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            throw new ArgumentException("Mime must not be empty.", nameof(mime));
        }

        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Filename must not be empty.", nameof(filename));
        }

        if (bytes is null || bytes.Length == 0)
        {
            throw new ArgumentException("Bytes must not be empty.", nameof(bytes));
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new RenderedAsset(formatKey, mime, filename, bytes, hash);
    }
}
