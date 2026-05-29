using FluentValidation;
using Incoders.Hive.Domain.Social.ClientMonthlyObjectives;

namespace Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.Upsert;

public sealed class UpsertClientMonthlyObjectiveCommandValidator : AbstractValidator<UpsertClientMonthlyObjectiveCommand>
{
    public UpsertClientMonthlyObjectiveCommandValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.client_monthly_objectives.errors.client_id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.client_monthly_objectives.errors.organization_required");

        RuleFor(x => x.Year)
            .InclusiveBetween(ClientMonthlyObjective.YearMin, ClientMonthlyObjective.YearMax)
            .WithMessage("social.client_monthly_objectives.errors.invalid_year");

        RuleFor(x => x.Month)
            .InclusiveBetween(ClientMonthlyObjective.MonthMin, ClientMonthlyObjective.MonthMax)
            .WithMessage("social.client_monthly_objectives.errors.invalid_month");

        RuleFor(x => x.Objectives)
            .NotEmpty().WithMessage("social.client_monthly_objectives.errors.objectives_required")
            .MaximumLength(ClientMonthlyObjective.ObjectivesMaxLength)
            .WithMessage("social.client_monthly_objectives.errors.objectives_too_long");

        RuleFor(x => x.Kpis)
            .Must(k => k is null || k.Count <= ClientMonthlyObjective.KpisMaxCount)
            .WithMessage("social.client_monthly_objectives.errors.kpis_too_many");
    }
}
