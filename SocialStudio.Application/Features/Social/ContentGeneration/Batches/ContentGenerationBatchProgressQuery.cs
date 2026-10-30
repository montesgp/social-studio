using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.GeneratePackage;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Batches;

/// <summary>
/// Live progress for one batch. Tenant-scoped: a cross-tenant id reads as not found.
/// </summary>
public sealed record ContentGenerationBatchProgressQuery(
    Guid BatchId,
    Guid OrganizationId) : IRequest<Result<ContentGenerationBatchProgress>>;

public sealed class ContentGenerationBatchProgressQueryHandler
    : IRequestHandler<ContentGenerationBatchProgressQuery, Result<ContentGenerationBatchProgress>>
{
    private readonly IContentGenerationBatchRepository _batches;

    public ContentGenerationBatchProgressQueryHandler(IContentGenerationBatchRepository batches)
    {
        ArgumentNullException.ThrowIfNull(batches);
        _batches = batches;
    }

    public async Task<Result<ContentGenerationBatchProgress>> Handle(
        ContentGenerationBatchProgressQuery query,
        CancellationToken cancellationToken)
    {
        var progress = await _batches.GetProgressAsync(query.BatchId, query.OrganizationId, cancellationToken);

        return progress is null
            ? Result.Failure<ContentGenerationBatchProgress>(BatchQueryErrors.BatchNotFound)
            : Result.Success(progress);
    }
}

/// <summary>
/// Every unfinished batch for the tenant — the feed behind the global progress bar, which is why it
/// returns progress for each rather than just the batch rows.
/// </summary>
public sealed record ActiveContentGenerationBatchesQuery(
    Guid OrganizationId) : IRequest<Result<IReadOnlyList<ContentGenerationBatchProgress>>>;

public sealed class ActiveContentGenerationBatchesQueryHandler
    : IRequestHandler<ActiveContentGenerationBatchesQuery, Result<IReadOnlyList<ContentGenerationBatchProgress>>>
{
    private readonly IContentGenerationBatchRepository _batches;

    public ActiveContentGenerationBatchesQueryHandler(IContentGenerationBatchRepository batches)
    {
        ArgumentNullException.ThrowIfNull(batches);
        _batches = batches;
    }

    public async Task<Result<IReadOnlyList<ContentGenerationBatchProgress>>> Handle(
        ActiveContentGenerationBatchesQuery query,
        CancellationToken cancellationToken)
    {
        var active = await _batches.ListActiveAsync(query.OrganizationId, cancellationToken);

        var progress = new List<ContentGenerationBatchProgress>(active.Count);

        foreach (var batch in active)
        {
            var item = await _batches.GetProgressAsync(batch.Id, query.OrganizationId, cancellationToken);

            // A batch that vanished between the two reads is simply not reported; the poll will pick
            // up reality on its next pass rather than failing the whole surface.
            if (item is not null)
            {
                progress.Add(item);
            }
        }

        return Result.Success<IReadOnlyList<ContentGenerationBatchProgress>>(progress);
    }
}

public static class BatchQueryErrors
{
    public static readonly Error BatchNotFound =
        Error.NotFound("content-gen.batch-not-found", "The specified batch was not found.");
}
