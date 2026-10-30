using SocialStudio.Application.Features.SystemSettings;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.SystemSettings.GetById;

public sealed record GetSystemSettingByIdQuery(Guid Id, Guid? RequiredTenantId = null)
    : IRequest<Result<SystemSettingResponse>>;
