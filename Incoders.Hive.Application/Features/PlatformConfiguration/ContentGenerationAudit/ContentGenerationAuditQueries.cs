using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.PlatformConfiguration.ContentGenerationAudit;

/// <summary>
/// Change 3 (job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1 incident): read-only per-job audit timeline for
/// sysadmins. Exposed under <c>/api/platform/content-generation-jobs</c>, gated by the same
/// PlatformAdmin policy as the rest of <c>/api/platform/integrations</c> — this surfaces execution
/// telemetry and asset state across ALL organizations, which must never be reachable by a normal
/// tenant user.
/// </summary>
public sealed record GetContentGenerationJobTimelineQuery(Guid JobId)
    : IRequest<Result<ContentGenerationJobTimeline>>;

/// <summary>Index the timeline view drills down from. Bounded — see <see cref="IContentGenerationAuditReader.ListJobsAsync"/>.</summary>
public sealed record ListContentGenerationJobsQuery(int Page = 1, int PageSize = 25)
    : IRequest<Result<ContentGenerationJobListPage>>;

public sealed class GetContentGenerationJobTimelineHandler
    : IRequestHandler<GetContentGenerationJobTimelineQuery, Result<ContentGenerationJobTimeline>>
{
    private readonly IContentGenerationAuditReader _reader;

    public GetContentGenerationJobTimelineHandler(IContentGenerationAuditReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<ContentGenerationJobTimeline>> Handle(
        GetContentGenerationJobTimelineQuery request,
        CancellationToken cancellationToken)
    {
        var timeline = await _reader.GetJobTimelineAsync(request.JobId, cancellationToken);
        return timeline is null
            ? Result.Failure<ContentGenerationJobTimeline>(ContentGenerationErrors.JobNotFound)
            : Result.Success(timeline);
    }
}

public sealed class ListContentGenerationJobsHandler
    : IRequestHandler<ListContentGenerationJobsQuery, Result<ContentGenerationJobListPage>>
{
    private readonly IContentGenerationAuditReader _reader;

    public ListContentGenerationJobsHandler(IContentGenerationAuditReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<ContentGenerationJobListPage>> Handle(
        ListContentGenerationJobsQuery request,
        CancellationToken cancellationToken)
    {
        var page = await _reader.ListJobsAsync(request.Page, request.PageSize, cancellationToken);
        return Result.Success(page);
    }
}
