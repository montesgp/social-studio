using FluentValidation;
using SocialStudio.Domain.Social.ContentPlans;

namespace SocialStudio.Application.Features.Social.ContentPlans.Upsert;

public sealed class UpsertContentPlanCommandValidator : AbstractValidator<UpsertContentPlanCommand>
{
    public UpsertContentPlanCommandValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.content_plans.errors.client_id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.content_plans.errors.organization_required");

        RuleFor(x => x.Year)
            .InclusiveBetween(Period.YearMin, Period.YearMax)
            .WithMessage("social.content_plans.errors.invalid_year");

        RuleFor(x => x.Month)
            .InclusiveBetween(Period.MonthMin, Period.MonthMax)
            .WithMessage("social.content_plans.errors.invalid_month");

        RuleFor(x => x.Objectives)
            .NotEmpty().WithMessage("social.content_plans.errors.objective_required")
            .MaximumLength(ContentPlan.ObjectivesMaxLength)
            .WithMessage("social.content_plans.errors.objective_too_long");

        RuleFor(x => x.Kpis)
            .Must(k => k is null || k.Count <= ContentPlan.KpisMaxCount)
            .WithMessage($"social.content_plans.errors.kpis_too_many");
    }
}
