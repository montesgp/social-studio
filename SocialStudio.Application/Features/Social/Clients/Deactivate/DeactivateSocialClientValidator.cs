using FluentValidation;

namespace SocialStudio.Application.Features.Social.Clients.Deactivate;

public sealed class DeactivateSocialClientValidator : AbstractValidator<DeactivateSocialClientCommand>
{
    public DeactivateSocialClientValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("social.clients.errors.id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.clients.errors.organization_required");
    }
}
