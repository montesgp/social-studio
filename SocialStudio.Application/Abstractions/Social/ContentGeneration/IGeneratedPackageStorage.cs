namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for storing and retrieving generated package ZIPs.
/// v1: <c>LocalGeneratedPackageStorage</c> — ephemeral filesystem.
/// Future: Google Drive adapter or Supabase Storage (clean swap, same port).
/// </summary>
public interface IGeneratedPackageStorage
{
    /// <summary>Stores the package content and returns a reference URI.</summary>
    Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct);

    /// <summary>Opens a read stream for the stored package.</summary>
    /// <exception cref="FileNotFoundException">When the package does not exist.</exception>
    Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct);

    /// <summary>Deletes the stored package.</summary>
    Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct);
}

/// <summary>Tenant-scoped storage key for a generated package.</summary>
public sealed record StorePackageKey(Guid OrganizationId, Guid JobId);

/// <summary>Opaque reference returned by <see cref="IGeneratedPackageStorage.StoreAsync"/>.</summary>
public sealed record StoredPackageRef(string Uri);
