using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.SystemSettings.Delete;

public sealed record DeleteSystemSettingCommand(Guid Id, Guid? RequiredTenantId = null) : IRequest<Result>;
