using Incoders.Hive.Application.Features.SystemSettings;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.SystemSettings;
using MediatR;

namespace Incoders.Hive.Application.Features.SystemSettings.Update;

public sealed record UpdateSystemSettingCommand(
    Guid Id,
    string Value,
    SystemSettingScope Scope,
    Guid? TenantId,
    Guid? UserId,
    Guid? RequiredTenantId = null) : IRequest<Result<SystemSettingResponse>>;
