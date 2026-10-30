using FluentValidation;

namespace SocialStudio.Application.Features.Social.Clients.GetAll;

public sealed class GetSocialClientsValidator : AbstractValidator<GetSocialClientsQuery>
{
    public GetSocialClientsValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThan(0).WithMessage("common.errors.page_positive");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("common.errors.page_size_range");
    }
}
