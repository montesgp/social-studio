using FluentValidation;

namespace SocialStudio.Application.Features.Social.Clients.GetById;

public sealed class GetSocialClientByIdValidator : AbstractValidator<GetSocialClientByIdQuery>
{
    public GetSocialClientByIdValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("social.clients.errors.id_required");

        // Null is the platform-admin "across every organization" lookup and is valid. An explicitly
        // supplied organization still has to be a real one.
        RuleFor(x => x.OrganizationId!.Value)
            .NotEmpty().WithMessage("social.clients.errors.organization_required")
            .When(x => x.OrganizationId.HasValue);
    }
}
