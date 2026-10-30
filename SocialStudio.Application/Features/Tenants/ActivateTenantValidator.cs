using FluentValidation;

namespace SocialStudio.Application.Features.Tenants;

public sealed class ActivateTenantValidator : AbstractValidator<ActivateTenantCommand>
{
    public ActivateTenantValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("tenants.errors.id_required");
    }
}
