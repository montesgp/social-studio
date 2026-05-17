using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.SystemSettings.Delete;

public sealed record DeleteSystemSettingCommand(Guid Id, Guid? RequiredTenantId = null) : IRequest<Result>;
