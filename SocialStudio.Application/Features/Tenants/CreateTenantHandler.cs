using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Tenants;
using MediatR;

namespace SocialStudio.Application.Features.Tenants;

public sealed class CreateTenantHandler : IRequestHandler<CreateTenantCommand, Result<TenantResponse>>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantMembershipService _memberships;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public CreateTenantHandler(
        ITenantRepository tenants,
        ITenantMembershipService memberships,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IClock clock)
    {
        _tenants = tenants;
        _memberships = memberships;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<TenantResponse>> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } ownerUserId)
        {
            return Result.Failure<TenantResponse>(TenantErrors.OwnerRequired);
        }

        var created = Tenant.Create(TenantId.New(), request.Name, ownerUserId, _clock.UtcNow);
        if (created.IsFailure)
        {
            return Result.Failure<TenantResponse>(created.Error);
        }

        await _tenants.AddAsync(created.Value, cancellationToken);
        await _memberships.AddAsync(
            new TenantMembership(created.Value.Id, ownerUserId, TenantRole.Owner, _clock.UtcNow),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(TenantResponse.FromDomain(created.Value));
    }
}
