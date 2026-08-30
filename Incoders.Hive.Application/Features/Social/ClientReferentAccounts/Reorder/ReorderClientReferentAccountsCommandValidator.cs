using FluentValidation;

namespace Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Reorder;

public sealed class ReorderClientReferentAccountsCommandValidator
    : AbstractValidator<ReorderClientReferentAccountsCommand>
{
    public ReorderClientReferentAccountsCommandValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.client_referent_accounts.errors.client_id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.client_referent_accounts.errors.organization_required");

        RuleFor(x => x.OrderedIds)
            .NotNull().WithMessage("social.client_referent_accounts.errors.ordered_ids_required")
            .Must(ids => ids is { Count: > 0 })
                .WithMessage("social.client_referent_accounts.errors.ordered_ids_empty")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
                .WithMessage("social.client_referent_accounts.errors.ordered_ids_duplicates");
    }
}
