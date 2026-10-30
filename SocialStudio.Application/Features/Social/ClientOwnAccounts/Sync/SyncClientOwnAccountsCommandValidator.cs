using FluentValidation;
using SocialStudio.Domain.Social.ClientOwnAccounts;

namespace SocialStudio.Application.Features.Social.ClientOwnAccounts.Sync;

public sealed class SyncClientOwnAccountsCommandValidator
    : AbstractValidator<SyncClientOwnAccountsCommand>
{
    public SyncClientOwnAccountsCommandValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.client_own_accounts.errors.organization_required");

        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.client_own_accounts.errors.client_required");

        RuleFor(x => x.Items)
            .NotNull().WithMessage("social.client_own_accounts.errors.items_required");

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.SocialNetworkId)
                    .NotEmpty().WithMessage("social.client_own_accounts.errors.social_network_required");

                item.RuleFor(x => x.ProfileUrl)
                    .NotEmpty().WithMessage("social.client_own_accounts.errors.profile_url_required")
                    .MaximumLength(ClientOwnAccount.ProfileUrlMaxLength)
                    .WithMessage("social.client_own_accounts.errors.profile_url_too_long");

                item.RuleFor(x => x.Handle)
                    .MaximumLength(ClientOwnAccount.HandleMaxLength)
                    .WithMessage("social.client_own_accounts.errors.handle_too_long");

                item.RuleFor(x => x.Label)
                    .MaximumLength(ClientOwnAccount.LabelMaxLength)
                    .WithMessage("social.client_own_accounts.errors.label_too_long");

                item.RuleFor(x => x.Notes)
                    .MaximumLength(ClientOwnAccount.NotesMaxLength)
                    .WithMessage("social.client_own_accounts.errors.notes_too_long");
            });
    }
}
