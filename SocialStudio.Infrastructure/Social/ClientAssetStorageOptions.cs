namespace SocialStudio.Infrastructure.Social;

public sealed class ClientAssetStorageOptions
{
    public const string SectionName = "ClientAssetStorage";

    public string RootPath { get; set; } = Path.Combine("storage", "client-assets");

    public string PublicBasePath { get; set; } = "/storage/client-assets";

    public long MaxImageSizeBytes { get; set; } = 8 * 1024 * 1024;

    public string[] AllowedImageContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp",
        "image/gif",
    ];

    public string ResolveRootPath(string contentRootPath)
    {
        if (Path.IsPathRooted(RootPath))
        {
            return RootPath;
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, RootPath));
    }

    public string NormalizedPublicBasePath()
    {
        var normalized = PublicBasePath.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "/storage/client-assets";
        }

        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.TrimEnd('/');
    }
}
