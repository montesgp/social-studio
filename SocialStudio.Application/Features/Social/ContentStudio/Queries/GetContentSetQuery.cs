using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentStudio.Queries;

public sealed record GetContentSetQuery(Guid OrganizationId, Guid SetId) : IRequest<Result<ContentSetResponse>>;

public sealed class GetContentSetQueryHandler
    : IRequestHandler<GetContentSetQuery, Result<ContentSetResponse>>
{
    private readonly IContentSetRepository _sets;

    public GetContentSetQueryHandler(IContentSetRepository sets)
    {
        _sets = sets;
    }

    public async Task<Result<ContentSetResponse>> Handle(GetContentSetQuery request, CancellationToken ct)
    {
        var setId = new ContentSetId(request.SetId);
        var set = await _sets.GetByIdAsync(request.OrganizationId, setId, ct);
        if (set is null)
        {
            return Result.Failure<ContentSetResponse>(ContentStudioErrors.SetNotFound);
        }

        var summary = await _sets.GetReviewSummaryAsync(request.OrganizationId, setId, ct);
        return Result.Success(ContentSetResponse.FromDomain(set, summary));
    }
}
