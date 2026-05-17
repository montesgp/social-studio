using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.SystemSettings;
using MediatR;

namespace Incoders.Hive.Application.Features.SystemSettings.Delete;

public sealed class DeleteSystemSettingHandler : IRequestHandler<DeleteSystemSettingCommand, Result>
{
    private readonly ISystemSettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteSystemSettingHandler(ISystemSettingRepository settings, IUnitOfWork unitOfWork)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteSystemSettingCommand request, CancellationToken cancellationToken)
    {
        var setting = await _settings.GetByIdAsync(new SystemSettingId(request.Id), cancellationToken);
        if (setting is null)
        {
            return Result.Failure(SystemSettingErrors.NotFound);
        }

        if (request.RequiredTenantId is { } tenantId
            && setting.Scope != SystemSettingScope.Global
            && setting.TenantId != tenantId)
        {
            return Result.Failure(SystemSettingErrors.CrossTenantAccessDenied);
        }

        _settings.Remove(setting);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
