using FluentValidation;

namespace SocialStudio.Application.Features.Tenants;

public sealed class DeleteTenantValidator : AbstractValidator<DeleteTenantCommand>
{
    public DeleteTenantValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("tenants.errors.id_required");
    }
}
