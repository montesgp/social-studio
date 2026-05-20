using FluentValidation;
using Incoders.Hive.Domain.Social.MonthlyObjectives;

namespace Incoders.Hive.Application.Features.Social.MonthlyObjectives.Upsert;

public sealed class UpsertMonthlyObjectiveCommandValidator : AbstractValidator<UpsertMonthlyObjectiveCommand>
{
    public UpsertMonthlyObjectiveCommandValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.monthly_objectives.errors.client_id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.monthly_objectives.errors.organization_required");

        RuleFor(x => x.Year)
            .InclusiveBetween(MonthlyObjective.YearMin, MonthlyObjective.YearMax)
            .WithMessage("social.monthly_objectives.errors.invalid_year");

        RuleFor(x => x.Month)
            .InclusiveBetween(MonthlyObjective.MonthMin, MonthlyObjective.MonthMax)
            .WithMessage("social.monthly_objectives.errors.invalid_month");

        RuleFor(x => x.Objectives)
            .NotEmpty().WithMessage("social.monthly_objectives.errors.objectives_required")
            .MaximumLength(MonthlyObjective.ObjectivesMaxLength)
            .WithMessage("social.monthly_objectives.errors.objectives_too_long");

        RuleFor(x => x.Kpis)
            .Must(k => k is null || k.Count <= MonthlyObjective.KpisMaxCount)
            .WithMessage("social.monthly_objectives.errors.kpis_too_many");
    }
}
