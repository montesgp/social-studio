using FluentValidation;

namespace SocialStudio.Application.Features.Social.Workflow;

public sealed class StartContentWorkflowValidator : AbstractValidator<StartContentWorkflowCommand>
{
    public StartContentWorkflowValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.workflow.errors.organization_required");

        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.workflow.errors.client_required");

        RuleFor(x => x.ClientName)
            .NotEmpty().WithMessage("social.workflow.errors.client_required")
            .MaximumLength(200).WithMessage("social.workflow.errors.client_required");

        RuleFor(x => x.SearchCriterion)
            .Must(ContentWorkflowCatalog.IsValidSearchCriterion)
            .WithMessage("social.workflow.errors.search_criterion_invalid");

        RuleFor(x => x.StrategyMode)
            .Must(ContentWorkflowCatalog.IsValidStrategyMode)
            .WithMessage("social.workflow.errors.strategy_mode_invalid");

        RuleFor(x => x.NetworkSources)
            .NotEmpty().WithMessage("social.workflow.errors.network_sources_required")
            .Must(values => values is not null && values.All(ContentWorkflowCatalog.IsValidNetworkSource))
            .WithMessage("social.workflow.errors.network_source_invalid");

        RuleFor(x => x.MaxResults!.Value)
            .InclusiveBetween(1, 100).WithMessage("social.workflow.errors.max_results_invalid")
            .When(x => x.MaxResults.HasValue);
    }
}

public sealed class GetContentWorkflowStatusValidator : AbstractValidator<GetContentWorkflowStatusQuery>
{
    public GetContentWorkflowStatusValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.workflow.errors.organization_required");

        RuleFor(x => x.WorkflowId)
            .NotEmpty().WithMessage("social.workflow.errors.not_found");
    }
}

public sealed class GetContentWorkflowResultsValidator : AbstractValidator<GetContentWorkflowResultsQuery>
{
    public GetContentWorkflowResultsValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.workflow.errors.organization_required");

        RuleFor(x => x.WorkflowId)
            .NotEmpty().WithMessage("social.workflow.errors.not_found");

        RuleFor(x => x.Page)
            .GreaterThan(0).WithMessage("common.errors.page_positive");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("common.errors.page_size_range");
    }
}

public sealed class SelectContentWorkflowResultsValidator : AbstractValidator<SelectContentWorkflowResultsCommand>
{
    public SelectContentWorkflowResultsValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.workflow.errors.organization_required");

        RuleFor(x => x.WorkflowId)
            .NotEmpty().WithMessage("social.workflow.errors.not_found");

        RuleFor(x => x.ResultIds)
            .NotEmpty().WithMessage("social.workflow.errors.selection_required");
    }
}

public sealed class GetContentWorkflowHistoryValidator : AbstractValidator<GetContentWorkflowHistoryQuery>
{
    public GetContentWorkflowHistoryValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.workflow.errors.organization_required");

        RuleFor(x => x.WorkflowId)
            .NotEmpty().WithMessage("social.workflow.errors.not_found");
    }
}

public sealed class ListContentWorkflowHistoryValidator : AbstractValidator<ListContentWorkflowHistoryQuery>
{
    public ListContentWorkflowHistoryValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.workflow.errors.organization_required");

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 50).WithMessage("common.errors.page_size_range");
    }
}
