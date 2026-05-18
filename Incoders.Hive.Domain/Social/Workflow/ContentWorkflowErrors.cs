using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Workflow;

public static class ContentWorkflowErrors
{
    public static readonly Error OrganizationRequired = Error.Validation(
        "social.workflow.organization_required",
        "social.workflow.errors.organization_required");

    public static readonly Error NotFound = Error.NotFound(
        "social.workflow.not_found",
        "social.workflow.errors.not_found");

    public static readonly Error ClientRequired = Error.Validation(
        "social.workflow.client_required",
        "social.workflow.errors.client_required");

    public static readonly Error SearchCriterionInvalid = Error.Validation(
        "social.workflow.search_criterion_invalid",
        "social.workflow.errors.search_criterion_invalid");

    public static readonly Error StrategyModeInvalid = Error.Validation(
        "social.workflow.strategy_mode_invalid",
        "social.workflow.errors.strategy_mode_invalid");

    public static readonly Error NetworkSourcesRequired = Error.Validation(
        "social.workflow.network_sources_required",
        "social.workflow.errors.network_sources_required");

    public static readonly Error NetworkSourceInvalid = Error.Validation(
        "social.workflow.network_source_invalid",
        "social.workflow.errors.network_source_invalid");

    public static readonly Error ActionInvalid = Error.Validation(
        "social.workflow.action_invalid",
        "social.workflow.errors.action_invalid");

    public static readonly Error SelectionRequired = Error.Validation(
        "social.workflow.selection_required",
        "social.workflow.errors.selection_required");

    public static readonly Error ResultNotFound = Error.NotFound(
        "social.workflow.result_not_found",
        "social.workflow.errors.result_not_found");

    public static readonly Error PackageSelectionRequired = Error.Validation(
        "social.workflow.package_selection_required",
        "social.workflow.errors.package_selection_required");
}
