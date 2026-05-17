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
}
