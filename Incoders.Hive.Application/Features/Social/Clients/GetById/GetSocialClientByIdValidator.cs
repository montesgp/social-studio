using FluentValidation;

namespace Incoders.Hive.Application.Features.Social.Clients.GetById;

public sealed class GetSocialClientByIdValidator : AbstractValidator<GetSocialClientByIdQuery>
{
    public GetSocialClientByIdValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("social.clients.errors.id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.clients.errors.organization_required");
    }
}
