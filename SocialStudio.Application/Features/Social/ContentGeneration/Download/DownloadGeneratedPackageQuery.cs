using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.GeneratePackage;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Download;

/// <summary>
/// Query to download the generated package ZIP for a completed job.
/// Tenant-scoped: cross-tenant job ids return NotFound.
/// </summary>
public sealed record DownloadGeneratedPackageQuery(
    Guid JobId,
    Guid OrganizationId,
    Guid? UserId = null) : IRequest<Result<DownloadGeneratedPackageResponse>>;

/// <summary>
/// Response containing the storage reference and an open read stream for the ZIP.
/// The caller is responsible for disposing the stream.
/// </summary>
public sealed record DownloadGeneratedPackageResponse(
    StoredPackageRef StorageRef,
    Stream ContentStream);

/// <summary>
/// Handles <see cref="DownloadGeneratedPackageQuery"/>:
/// - Returns NotFound when the job does not exist or belongs to another tenant.
/// - Returns NotFound when the job is not yet completed.
/// - Returns the storage ref + open stream when the package is ready.
/// </summary>
public sealed class DownloadGeneratedPackageQueryHandler
    : IRequestHandler<DownloadGeneratedPackageQuery, Result<DownloadGeneratedPackageResponse>>
{
    private static readonly IReadOnlySet<string> CompletedStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Completed", "Partial" };

    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IGeneratedPackageRepository _packageRepository;
    private readonly IGeneratedPackageStorage _packageStorage;
    private readonly IClock? _clock;

    public DownloadGeneratedPackageQueryHandler(
        IContentGenerationJobRepository jobRepository,
        IGeneratedPackageRepository packageRepository,
        IGeneratedPackageStorage packageStorage,
        IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(packageRepository);
        ArgumentNullException.ThrowIfNull(packageStorage);

        _jobRepository = jobRepository;
        _packageRepository = packageRepository;
        _packageStorage = packageStorage;
        _clock = clock;
    }

    public async Task<Result<DownloadGeneratedPackageResponse>> Handle(
        DownloadGeneratedPackageQuery query,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped lookup
        var job = await _jobRepository.GetByIdAsync(
            query.JobId,
            query.OrganizationId,
            cancellationToken);

        if (job is null)
        {
            return Result.Failure<DownloadGeneratedPackageResponse>(
                ContentGenerationErrors.JobNotFound);
        }

        // Only completed or partial jobs have downloadable packages
        if (!CompletedStatuses.Contains(job.Status))
        {
            return Result.Failure<DownloadGeneratedPackageResponse>(
                ContentGenerationErrors.PackageNotReady);
        }

        // Load the package row (1:1 with job)
        var package = await _packageRepository.GetByJobIdAsync(
            job.Id,
            job.OrganizationId,
            cancellationToken);

        if (package is null)
        {
            return Result.Failure<DownloadGeneratedPackageResponse>(
                ContentGenerationErrors.PackageNotReady);
        }

        // Open the stored ZIP stream
        var storageRef = new StoredPackageRef(package.StorageUri);
        var stream = await _packageStorage.OpenAsync(storageRef, cancellationToken);

        await _packageRepository.MarkDownloadedAsync(
            job.Id,
            job.OrganizationId,
            query.UserId,
            _clock?.UtcNow ?? DateTime.UtcNow,
            cancellationToken);

        return Result.Success(new DownloadGeneratedPackageResponse(storageRef, stream));
    }
}
