using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// Starts a profile analysis: validates source exclusivity + pre-flights + creates the record
/// (status Requested), then enqueues the heavy scrape + AI work to run in the background.
/// Returns immediately with the analysis id/status so the client can poll.
/// The IProfileScrapeResolver is NOT called during this stage — only during the background run.
/// </summary>
public sealed class AnalyzeProfileHandler
    : IRequestHandler<AnalyzeProfileCommand, Result<AnalyzeProfileStatusResponse>>
{
    private readonly IProfileAnalysisRepository _repository;
    private readonly IClientOwnAccountRepository _ownAccountRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IProfileAnalysisJobQueue _queue;
    private readonly ILogger<AnalyzeProfileHandler> _logger;

    public AnalyzeProfileHandler(
        IProfileAnalysisRepository repository,
        IClientOwnAccountRepository ownAccountRepo,
        IUnitOfWork unitOfWork,
        IClock clock,
        IProfileAnalysisJobQueue queue,
        ILogger<AnalyzeProfileHandler>? logger = null)
    {
        _repository = repository;
        _ownAccountRepo = ownAccountRepo;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _queue = queue;
        _logger = logger ?? NullLogger<AnalyzeProfileHandler>.Instance;
    }

    public async Task<Result<AnalyzeProfileStatusResponse>> Handle(
        AnalyzeProfileCommand request,
        CancellationToken cancellationToken)
    {
        // ── PA-03: Source exclusivity ──────────────────────────────────────────
        var hasClientPath = request.ClientId.HasValue && request.OwnAccountId.HasValue;
        var hasUrlPath = !string.IsNullOrWhiteSpace(request.ProfileUrl);

        if (hasClientPath && hasUrlPath)
        {
            return Result.Failure<AnalyzeProfileStatusResponse>(
                ProfileAnalysisErrors.ProfileSourceMutuallyExclusive);
        }

        if (!hasClientPath && !hasUrlPath)
        {
            return Result.Failure<AnalyzeProfileStatusResponse>(
                ProfileAnalysisErrors.ProfileSourceRequired);
        }

        var now = _clock.UtcNow;
        ProfileAnalysis analysis;

        if (hasClientPath)
        {
            // ── PA-01: Validate own account exists ─────────────────────────────
            var ownAccount = await _ownAccountRepo.GetByIdAsync(
                new ClientOwnAccountId(request.OwnAccountId!.Value),
                request.OrganizationId,
                cancellationToken);

            if (ownAccount is null)
            {
                return Result.Failure<AnalyzeProfileStatusResponse>(
                    ProfileAnalysisErrors.OwnAccountNotFound);
            }

            var profileUrlOrHandle = ownAccount.Handle
                ?? ownAccount.ProfileUrl;

            analysis = ProfileAnalysis.CreateFromClient(
                request.OrganizationId,
                request.ClientId!.Value,
                request.OwnAccountId!.Value,
                profileUrlOrHandle,
                now);
        }
        else
        {
            // ── PA-02: URL path ────────────────────────────────────────────────
            analysis = ProfileAnalysis.CreateFromUrl(
                request.OrganizationId,
                request.ProfileUrl!,
                now);
        }

        await _repository.AddAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Profile Analyzer: queued analysis {AnalysisId} for {ProfileUrlOrHandle} (org {OrganizationId}).",
            analysis.Id,
            analysis.ProfileUrlOrHandle,
            analysis.OrganizationId);

        await _queue.EnqueueAsync(
            new ProfileAnalysisJob(analysis.Id, analysis.OrganizationId),
            cancellationToken);

        return Result.Success(AnalyzeProfileStatusResponse.FromRecord(analysis));
    }
}

/// <summary>Reads the current status/result of a profile analysis for polling (PA-15).</summary>
public sealed class GetProfileAnalysisHandler
    : IRequestHandler<GetProfileAnalysisQuery, Result<AnalyzeProfileStatusResponse>>
{
    private readonly IProfileAnalysisRepository _repository;

    public GetProfileAnalysisHandler(IProfileAnalysisRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<AnalyzeProfileStatusResponse>> Handle(
        GetProfileAnalysisQuery request,
        CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetByIdAsync(
            request.AnalysisId,
            request.OrganizationId,
            cancellationToken);

        return analysis is null
            ? Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.NotFound)
            : Result.Success(AnalyzeProfileStatusResponse.FromRecord(analysis));
    }
}
