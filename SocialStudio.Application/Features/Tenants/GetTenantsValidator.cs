using FluentValidation;

namespace SocialStudio.Application.Features.Tenants;

public sealed class GetTenantsValidator : AbstractValidator<GetTenantsQuery>
{
    public GetTenantsValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThan(0).WithMessage("common.errors.page_positive");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("common.errors.page_size_range");
    }
}
