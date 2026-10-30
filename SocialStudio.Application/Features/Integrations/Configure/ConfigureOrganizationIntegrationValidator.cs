using FluentValidation;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.Configure;

public sealed class ConfigureOrganizationIntegrationValidator : AbstractValidator<ConfigureOrganizationIntegrationCommand>
{
    public ConfigureOrganizationIntegrationValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("integrations.errors.organization_required");

        RuleFor(x => x.ProviderKey)
            .NotEmpty().WithMessage("integrations.errors.provider_key_required");

        RuleForEach(x => x.Credentials)
            .ChildRules(rule =>
            {
                rule.RuleFor(entry => entry.Key)
                    .NotEmpty().WithMessage("integrations.errors.credential_key_required");
            });
    }
}
