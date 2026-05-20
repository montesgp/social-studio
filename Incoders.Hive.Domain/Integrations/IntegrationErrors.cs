using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Integrations;

public static class IntegrationErrors
{
    public static readonly Error ProviderKeyRequired = Error.Validation(
        "integrations.provider_key_required",
        "integrations.errors.provider_key_required");

    public static readonly Error ProviderNameRequired = Error.Validation(
        "integrations.provider_name_required",
        "integrations.errors.provider_name_required");

    public static readonly Error ProviderIconKeyRequired = Error.Validation(
        "integrations.provider_icon_key_required",
        "integrations.errors.provider_icon_key_required");

    public static readonly Error ProviderModeRequired = Error.Validation(
        "integrations.provider_mode_required",
        "integrations.errors.provider_mode_required");

    public static readonly Error ProviderCredentialFieldDuplicate = Error.Validation(
        "integrations.provider_credential_field_duplicate",
        "integrations.errors.provider_credential_field_duplicate");

    public static readonly Error ProviderNotFound = Error.NotFound(
        "integrations.provider_not_found",
        "integrations.errors.provider_not_found");

    public static readonly Error OrganizationRequired = Error.Validation(
        "integrations.organization_required",
        "integrations.errors.organization_required");

    public static readonly Error IntegrationNotFound = Error.NotFound(
        "integrations.organization_integration_not_found",
        "integrations.errors.organization_integration_not_found");

    public static readonly Error ConnectionModeNotSupported = Error.Validation(
        "integrations.connection_mode_not_supported",
        "integrations.errors.connection_mode_not_supported");

    public static readonly Error ReadOnlySystemDefaultOnly = Error.Validation(
        "integrations.readonly_requires_system_default",
        "integrations.errors.readonly_requires_system_default");

    public static readonly Error CredentialKeyRequired = Error.Validation(
        "integrations.credential_key_required",
        "integrations.errors.credential_key_required");

    public static readonly Error CredentialValueRequired = Error.Validation(
        "integrations.credential_value_required",
        "integrations.errors.credential_value_required");

    public static readonly Error RequiredCredentialMissing = Error.Validation(
        "integrations.required_credential_missing",
        "integrations.errors.required_credential_missing");

    public static readonly Error IntegrationDisabled = Error.Validation(
        "integrations.integration_disabled",
        "integrations.errors.integration_disabled");

    public static readonly Error IntegrationNotConnected = Error.Validation(
        "integrations.integration_not_connected",
        "integrations.errors.integration_not_connected");

    public static readonly Error DefaultAiProviderInvalidCategory = Error.Validation(
        "integrations.default_ai_provider_invalid_category",
        "integrations.errors.default_ai_provider_invalid_category");

    public static readonly Error DefaultAiProviderNotConnected = Error.Validation(
        "integrations.default_ai_provider_not_connected",
        "integrations.errors.default_ai_provider_not_connected");

    public static readonly Error ConnectionTestFailed = Error.Failure(
        "integrations.connection_test_failed",
        "integrations.errors.connection_test_failed");

    public static readonly Error RoutingPolicyOrganizationRequired = Error.Validation(
        "integrations.routing_policy_organization_required",
        "integrations.errors.routing_policy_organization_required");

    public static readonly Error RoutingPolicyFlowRequired = Error.Validation(
        "integrations.routing_policy_flow_required",
        "integrations.errors.routing_policy_flow_required");

    public static readonly Error RoutingPolicyCapabilityRequired = Error.Validation(
        "integrations.routing_policy_capability_required",
        "integrations.errors.routing_policy_capability_required");

    public static readonly Error RoutingPolicyRequiresEnabledCandidate = Error.Validation(
        "integrations.routing_policy_requires_enabled_candidate",
        "integrations.errors.routing_policy_requires_enabled_candidate");

    public static readonly Error RoutingPolicyDuplicatePriority = Error.Validation(
        "integrations.routing_policy_duplicate_priority",
        "integrations.errors.routing_policy_duplicate_priority");

    public static readonly Error RoutingCandidateProviderRequired = Error.Validation(
        "integrations.routing_candidate_provider_required",
        "integrations.errors.routing_candidate_provider_required");

    public static readonly Error RoutingCandidatePriorityInvalid = Error.Validation(
        "integrations.routing_candidate_priority_invalid",
        "integrations.errors.routing_candidate_priority_invalid");

    public static readonly Error RoutingCandidateAttemptsInvalid = Error.Validation(
        "integrations.routing_candidate_attempts_invalid",
        "integrations.errors.routing_candidate_attempts_invalid");

    public static readonly Error RoutingCandidateTimeoutInvalid = Error.Validation(
        "integrations.routing_candidate_timeout_invalid",
        "integrations.errors.routing_candidate_timeout_invalid");

    public static readonly Error RoutingCandidateManagedFallbackNotAllowed = Error.Validation(
        "integrations.routing_candidate_managed_fallback_not_allowed",
        "integrations.errors.routing_candidate_managed_fallback_not_allowed");

    public static readonly Error ProviderFailureDiagnosticInvalid = Error.Validation(
        "integrations.provider_failure_diagnostic_invalid",
        "integrations.errors.provider_failure_diagnostic_invalid");

    public static readonly Error RoutingPolicyNotConfigured = Error.Validation(
        "integrations.routing_policy_not_configured",
        "integrations.errors.routing_policy_not_configured");

    public static readonly Error ProviderTimeout = Error.Failure(
        "integrations.provider_timeout",
        "integrations.errors.provider_timeout");

    public static readonly Error ProviderRateLimited = Error.Failure(
        "integrations.provider_rate_limited",
        "integrations.errors.provider_rate_limited");

    public static readonly Error ProviderCredentialUnavailable = Error.Validation(
        "integrations.provider_credential_unavailable",
        "integrations.errors.provider_credential_unavailable");

    public static readonly Error ExternalProviderUnsupported = Error.Validation(
        "integrations.external_provider_unsupported",
        "integrations.errors.external_provider_unsupported");

    public static readonly Error ExternalProviderUnavailable = Error.Failure(
        "integrations.external_provider_unavailable",
        "integrations.errors.external_provider_unavailable");

    public static readonly Error AiModelRequired = Error.Validation(
        "integrations.ai_model_required",
        "integrations.errors.ai_model_required");

    public static readonly Error AiJsonInvalid = Error.Failure(
        "integrations.ai_json_invalid",
        "integrations.errors.ai_json_invalid");
}
