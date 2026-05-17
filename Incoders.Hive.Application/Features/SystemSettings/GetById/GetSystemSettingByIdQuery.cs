using Incoders.Hive.Application.Features.SystemSettings;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.SystemSettings.GetById;

public sealed record GetSystemSettingByIdQuery(Guid Id, Guid? RequiredTenantId = null)
    : IRequest<Result<SystemSettingResponse>>;
