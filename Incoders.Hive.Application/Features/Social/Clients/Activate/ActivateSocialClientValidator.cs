using FluentValidation;

namespace Incoders.Hive.Application.Features.Social.Clients.Activate;

public sealed class ActivateSocialClientValidator : AbstractValidator<ActivateSocialClientCommand>
{
    public ActivateSocialClientValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("social.clients.errors.id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.clients.errors.organization_required");
    }
}
