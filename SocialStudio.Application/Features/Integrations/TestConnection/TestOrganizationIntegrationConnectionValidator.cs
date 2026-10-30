using FluentValidation;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.TestConnection;

public sealed class TestOrganizationIntegrationConnectionValidator
    : AbstractValidator<TestOrganizationIntegrationConnectionCommand>
{
    public TestOrganizationIntegrationConnectionValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("integrations.errors.organization_required");

        RuleFor(x => x.ProviderKey)
            .NotEmpty().WithMessage("integrations.errors.provider_key_required");
    }
}
