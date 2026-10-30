using FluentValidation;
using SocialStudio.Domain.Social.ClientCompetitorAccounts;

namespace SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Sync;

public sealed class SyncClientCompetitorAccountsCommandValidator
    : AbstractValidator<SyncClientCompetitorAccountsCommand>
{
    public SyncClientCompetitorAccountsCommandValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.client_competitor_accounts.errors.organization_required");

        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.client_competitor_accounts.errors.client_required");

        RuleFor(x => x.Items)
            .NotNull().WithMessage("social.client_competitor_accounts.errors.items_required");

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.SocialNetworkId)
                    .NotEmpty().WithMessage("social.client_competitor_accounts.errors.social_network_required");

                item.RuleFor(x => x.ProfileUrl)
                    .NotEmpty().WithMessage("social.client_competitor_accounts.errors.profile_url_required")
                    .MaximumLength(ClientCompetitorAccount.ProfileUrlMaxLength)
                    .WithMessage("social.client_competitor_accounts.errors.profile_url_too_long");

                item.RuleFor(x => x.Handle)
                    .MaximumLength(ClientCompetitorAccount.HandleMaxLength)
                    .WithMessage("social.client_competitor_accounts.errors.handle_too_long");

                item.RuleFor(x => x.Label)
                    .MaximumLength(ClientCompetitorAccount.LabelMaxLength)
                    .WithMessage("social.client_competitor_accounts.errors.label_too_long");

                item.RuleFor(x => x.Notes)
                    .MaximumLength(ClientCompetitorAccount.NotesMaxLength)
                    .WithMessage("social.client_competitor_accounts.errors.notes_too_long");
            });
    }
}
