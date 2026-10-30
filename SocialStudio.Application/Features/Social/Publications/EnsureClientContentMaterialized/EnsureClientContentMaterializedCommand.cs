using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ContentStudio.Materialize;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Publications.EnsureClientContentMaterialized;

/// <summary>
/// Lazy materialization (design §5.3): ensures every completed analysis for a client has a
/// materialized Content Studio set BEFORE the Bank listing query runs, so "all content for a client"
/// holds true without a background worker. Pure orchestration — loops the client's completed analyses
/// and re-runs the EXISTING idempotent <see cref="MaterializeAnalysisCommand"/> for each; introduces no
/// new materialization logic.
/// </summary>
public sealed record EnsureClientContentMaterializedCommand(Guid OrganizationId, Guid ClientId)
    : IRequest<Result>;

public sealed class EnsureClientContentMaterializedCommandHandler
    : IRequestHandler<EnsureClientContentMaterializedCommand, Result>
{
    private readonly ISingleContentAnalysisRepository _analyses;
    private readonly ISender _sender;

    public EnsureClientContentMaterializedCommandHandler(ISingleContentAnalysisRepository analyses, ISender sender)
    {
        _analyses = analyses;
        _sender = sender;
    }

    public async Task<Result> Handle(EnsureClientContentMaterializedCommand request, CancellationToken ct)
    {
        var completed = await _analyses.ListCompletedByClientAsync(request.OrganizationId, request.ClientId, ct);

        foreach (var analysis in completed)
        {
            // MaterializeAnalysisCommand is idempotent (re-running returns the existing set without
            // duplicating members) — this loop never needs to pre-check "already materialized" itself.
            await _sender.Send(
                new MaterializeAnalysisCommand(request.OrganizationId, request.ClientId, analysis.Id), ct);
        }

        return Result.Success();
    }
}
