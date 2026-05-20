using FluentValidation;

namespace Incoders.Hive.Application.Features.Integrations.RoutingPolicies;

public sealed class UpsertOrganizationRoutingPolicyValidator : AbstractValidator<UpsertOrganizationRoutingPolicyCommand>
{
    public UpsertOrganizationRoutingPolicyValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("integrations.errors.organization_required");

        RuleFor(x => x.FlowKey)
            .NotEmpty().WithMessage("integrations.errors.routing_policy_flow_required");

        RuleFor(x => x.CapabilityKey)
            .NotEmpty().WithMessage("integrations.errors.routing_policy_capability_required");

        RuleFor(x => x.Candidates)
            .NotNull().WithMessage("integrations.errors.routing_policy_requires_enabled_candidate")
            .Must(candidates => candidates.Count > 0).WithMessage("integrations.errors.routing_policy_requires_enabled_candidate");

        RuleForEach(x => x.Candidates)
            .ChildRules(rule =>
            {
                rule.RuleFor(x => x.ProviderKey)
                    .NotEmpty().WithMessage("integrations.errors.routing_candidate_provider_required");

                rule.RuleFor(x => x.Priority)
                    .GreaterThan(0).WithMessage("integrations.errors.routing_candidate_priority_invalid");

                rule.RuleFor(x => x.MaxAttempts)
                    .GreaterThan(0).WithMessage("integrations.errors.routing_candidate_attempts_invalid");

                rule.RuleFor(x => x.TimeoutMs)
                    .GreaterThan(0).WithMessage("integrations.errors.routing_candidate_timeout_invalid");
            });
    }
}
