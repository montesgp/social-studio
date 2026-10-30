using FluentValidation;
using SocialStudio.Domain.Tenants;

namespace SocialStudio.Application.Features.Tenants;

public sealed class UpdateTenantValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("tenants.errors.id_required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("tenants.errors.name_required")
            .MaximumLength(Tenant.NameMaxLength).WithMessage("tenants.errors.name_too_long");
    }
}
