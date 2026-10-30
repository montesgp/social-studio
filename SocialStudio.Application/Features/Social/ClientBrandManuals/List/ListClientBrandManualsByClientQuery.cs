using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientBrandManuals.List;

public sealed record ListClientBrandManualsByClientQuery(
    Guid OrganizationId,
    Guid ClientId,
    bool IncludeInactive,
    int Page,
    int PageSize) : IRequest<Result<PagedClientBrandManualsResponse>>;
