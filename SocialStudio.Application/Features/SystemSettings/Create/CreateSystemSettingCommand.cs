using SocialStudio.Application.Features.SystemSettings;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.SystemSettings;
using MediatR;

namespace SocialStudio.Application.Features.SystemSettings.Create;

public sealed record CreateSystemSettingCommand(
    string Key,
    string Value,
    SystemSettingScope Scope,
    Guid? TenantId,
    Guid? UserId) : IRequest<Result<SystemSettingResponse>>;
