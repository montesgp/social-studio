using FluentValidation;

namespace SocialStudio.Application.Features.Social.Clients.Delete;

public sealed class DeleteSocialClientValidator : AbstractValidator<DeleteSocialClientCommand>
{
    public DeleteSocialClientValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("social.clients.errors.id_required");
    }
}
