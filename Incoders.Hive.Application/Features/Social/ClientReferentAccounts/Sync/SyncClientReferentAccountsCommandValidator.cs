using FluentValidation;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;

namespace Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Sync;

public sealed class SyncClientReferentAccountsCommandValidator
    : AbstractValidator<SyncClientReferentAccountsCommand>
{
    public SyncClientReferentAccountsCommandValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.client_referent_accounts.errors.organization_required");

        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.client_referent_accounts.errors.client_required");

        RuleFor(x => x.Items)
            .NotNull().WithMessage("social.client_referent_accounts.errors.items_required");

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.SocialNetworkId)
                    .NotEmpty().WithMessage("social.client_referent_accounts.errors.social_network_required");

                item.RuleFor(x => x.ProfileUrl)
                    .NotEmpty().WithMessage("social.client_referent_accounts.errors.profile_url_required")
                    .MaximumLength(ClientReferentAccount.ProfileUrlMaxLength)
                    .WithMessage("social.client_referent_accounts.errors.profile_url_too_long");

                item.RuleFor(x => x.Handle)
                    .MaximumLength(ClientReferentAccount.HandleMaxLength)
                    .WithMessage("social.client_referent_accounts.errors.handle_too_long");

                item.RuleFor(x => x.Label)
                    .MaximumLength(ClientReferentAccount.LabelMaxLength)
                    .WithMessage("social.client_referent_accounts.errors.label_too_long");

                item.RuleFor(x => x.Notes)
                    .MaximumLength(ClientReferentAccount.NotesMaxLength)
                    .WithMessage("social.client_referent_accounts.errors.notes_too_long");
            });
    }
}
