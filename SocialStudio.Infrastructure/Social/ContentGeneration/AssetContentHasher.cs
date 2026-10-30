using System.Security.Cryptography;

namespace SocialStudio.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Shared SHA-256 content hashing used by every <c>IGeneratedAssetStorage</c> adapter
/// (<see cref="SupabaseStorageAdapter"/>, <see cref="LocalFileGeneratedAssetStorage"/>).
/// Keeping the algorithm in one place guarantees reuse-by-hash detection behaves identically
/// regardless of which storage provider is active (dev Local vs. prod Supabase).
/// </summary>
internal static class AssetContentHasher
{
    /// <summary>Computes the lowercase hex-encoded SHA-256 digest of <paramref name="bytes"/>.</summary>
    public static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
