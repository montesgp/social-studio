using FluentValidation;
using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Application.Features.Social.Clients.Create;

public sealed class CreateSocialClientValidator : AbstractValidator<CreateSocialClientCommand>
{
    public CreateSocialClientValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.clients.errors.organization_required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("social.clients.errors.name_required")
            .MaximumLength(Client.NameMaxLength).WithMessage("social.clients.errors.name_too_long");

        RuleFor(x => x.Description)
            .MaximumLength(Client.DescriptionMaxLength).WithMessage("social.clients.errors.description_too_long");

        RuleFor(x => x.SectorOrNiche)
            .MaximumLength(Client.SectorOrNicheMaxLength).WithMessage("social.clients.errors.sector_or_niche_too_long");
    }
}
