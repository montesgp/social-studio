using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Port for loading a frozen brand snapshot for a client at job-start time.
/// The snapshot is immutable for the lifetime of the content generation job;
/// subsequent brand manual edits must not affect in-progress or completed jobs.
/// </summary>
public interface IClientBrandSnapshotLoader
{
    /// <summary>
    /// Loads and freezes the active brand context for the given client.
    /// When no brand data exists, returns a fully-inferred <see cref="BrandSnapshot"/>
    /// where all fields have <c>IsInferred = true</c>.
    /// This method MUST NOT throw when brand data is absent.
    /// </summary>
    Task<BrandSnapshot> LoadAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default);
}
