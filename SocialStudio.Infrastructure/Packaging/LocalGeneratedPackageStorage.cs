using SocialStudio.Application.Abstractions.Social.ContentGeneration;

namespace SocialStudio.Infrastructure.Packaging;

/// <summary>
/// Ephemeral local filesystem storage for generated package ZIPs.
/// <para>
/// Storage layout: <c>{root}/{organizationId}/{jobId}/package.zip</c>
/// </para>
/// <para>
/// Ephemeral storage. No retention policy in v1.
/// Future Google Drive adapter will implement <see cref="IGeneratedPackageStorage"/>.
/// </para>
/// </summary>
public sealed class LocalGeneratedPackageStorage : IGeneratedPackageStorage
{
    private readonly string _rootPath;

    /// <summary>Creates storage backed by the given root directory.</summary>
    public LocalGeneratedPackageStorage(string rootPath)
    {
        _rootPath = rootPath;
    }

    public async Task<StoredPackageRef> StoreAsync(
        Stream content,
        StorePackageKey key,
        CancellationToken ct)
    {
        var dir = BuildDirectory(key.OrganizationId, key.JobId);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "package.zip");

        await using var target = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        await content.CopyToAsync(target, ct);

        return new StoredPackageRef(path);
    }

    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct)
    {
        if (!File.Exists(refKey.Uri))
        {
            throw new FileNotFoundException($"Generated package not found: {refKey.Uri}", refKey.Uri);
        }

        Stream stream = new FileStream(
            refKey.Uri,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct)
    {
        if (File.Exists(refKey.Uri))
        {
            File.Delete(refKey.Uri);
        }

        return Task.CompletedTask;
    }

    private string BuildDirectory(Guid organizationId, Guid jobId) =>
        Path.Combine(
            _rootPath,
            organizationId.ToString(),
            jobId.ToString());
}
