using SocialStudio.Application.Features.SystemSettings;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.SystemSettings;
using MediatR;

namespace SocialStudio.Application.Features.SystemSettings.Update;

public sealed record UpdateSystemSettingCommand(
    Guid Id,
    string Value,
    SystemSettingScope Scope,
    Guid? TenantId,
    Guid? UserId,
    Guid? RequiredTenantId = null) : IRequest<Result<SystemSettingResponse>>;
