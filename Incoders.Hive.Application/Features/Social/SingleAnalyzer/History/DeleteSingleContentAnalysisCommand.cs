using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.SingleAnalyzer.History;

/// <summary>
/// Hard-deletes an analysis owned by the current user. Cleans up:
/// - the analysis row (and via DB-level FK CASCADE the integration_execution_attempts)
/// - all linked content_generation_jobs (and via FK CASCADE their format_results + generated_packages rows)
/// - the physical package ZIPs in <see cref="IGeneratedPackageStorage"/>
///
/// The user can only delete their own analyses (ownership check in the repo).
/// </summary>
public sealed record DeleteSingleContentAnalysisCommand(
    Guid AnalysisId,
    Guid OrganizationId,
    Guid UserId) : IRequest<Result>;

public sealed class DeleteSingleContentAnalysisHandler
    : IRequestHandler<DeleteSingleContentAnalysisCommand, Result>
{
    private readonly ISingleContentAnalysisRepository _analyses;
    private readonly IGeneratedPackageStorage _packageStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteSingleContentAnalysisHandler> _logger;

    public DeleteSingleContentAnalysisHandler(
        ISingleContentAnalysisRepository analyses,
        IGeneratedPackageStorage packageStorage,
        IUnitOfWork unitOfWork,
        ILogger<DeleteSingleContentAnalysisHandler> logger)
    {
        _analyses = analyses;
        _packageStorage = packageStorage;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(
        DeleteSingleContentAnalysisCommand request,
        CancellationToken cancellationToken)
    {
        var storageUris = await _analyses.DeleteOwnedByUserAsync(
            request.AnalysisId,
            request.OrganizationId,
            request.UserId,
            cancellationToken);

        if (storageUris is null)
        {
            return Result.Failure(SingleAnalyzerErrors.NotFound);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // File cleanup is best-effort: if a ZIP is already missing on disk (manual cleanup,
        // crashed prior run, etc.) we don't want to fail the whole delete — the DB rows are
        // already gone, and a stranded file with no row pointing at it is harmless garbage.
        foreach (var uri in storageUris)
        {
            try
            {
                await _packageStorage.DeleteAsync(new StoredPackageRef(uri), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete generated package file '{StorageUri}' for analysis {AnalysisId}. " +
                    "DB rows were deleted successfully; the file is now orphaned.",
                    uri,
                    request.AnalysisId);
            }
        }

        return Result.Success();
    }
}
