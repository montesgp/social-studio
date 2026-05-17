using FluentValidation;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Application.Features.Social.Clients.Update;

public sealed class UpdateSocialClientValidator : AbstractValidator<UpdateSocialClientCommand>
{
    public UpdateSocialClientValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("social.clients.errors.id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.clients.errors.organization_required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("social.clients.errors.name_required")
            .MaximumLength(Client.NameMaxLength).WithMessage("social.clients.errors.name_too_long");

        RuleFor(x => x.Description)
            .MaximumLength(Client.DescriptionMaxLength).WithMessage("social.clients.errors.description_too_long");
    }
}
