using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Tenants;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

public sealed class DeactivateTenantHandler : IRequestHandler<DeactivateTenantCommand, Result<TenantResponse>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public DeactivateTenantHandler(ITenantRepository tenants, IUnitOfWork unitOfWork, IClock clock)
    {
        _tenants = tenants;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TenantResponse>> Handle(DeactivateTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(new TenantId(request.Id), cancellationToken);
        if (tenant is null)
        {
            return Result.Failure<TenantResponse>(TenantErrors.NotFound);
        }

        var deactivated = tenant.Deactivate(_clock.UtcNow);
        if (deactivated.IsFailure)
        {
            return Result.Failure<TenantResponse>(deactivated.Error);
        }

        _tenants.Update(tenant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(TenantResponse.FromDomain(tenant));
    }
}
