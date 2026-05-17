using Incoders.Hive.Application.Features.SystemSettings;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.SystemSettings;
using MediatR;

namespace Incoders.Hive.Application.Features.SystemSettings.Create;

public sealed record CreateSystemSettingCommand(
    string Key,
    string Value,
    SystemSettingScope Scope,
    Guid? TenantId,
    Guid? UserId) : IRequest<Result<SystemSettingResponse>>;
