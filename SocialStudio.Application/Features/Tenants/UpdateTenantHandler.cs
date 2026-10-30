using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Tenants;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

public sealed class UpdateTenantHandler : IRequestHandler<UpdateTenantCommand, Result<TenantResponse>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateTenantHandler(ITenantRepository tenants, IUnitOfWork unitOfWork, IClock clock)
    {
        _tenants = tenants;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TenantResponse>> Handle(UpdateTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(new TenantId(request.Id), cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<TenantResponse>(TenantErrors.NotFound);
        }

        var renamed = tenant.Rename(request.Name, _clock.UtcNow);
        if (renamed.IsFailure)
        {
            return Result.Failure<TenantResponse>(renamed.Error);
        }

        _tenants.Update(tenant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(TenantResponse.FromDomain(tenant));
    }
}
