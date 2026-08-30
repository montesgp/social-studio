using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.DiscoveryBankIntelligence;
using Incoders.Hive.Domain.Social.DiscoveryBankIntelligence;
using MediatR;

namespace Incoders.Hive.ApiService.Endpoints.Social;

/// <summary>
/// The AI Analysis tool: read the discovery candidate bank, and ask questions of it.
///
/// Both endpoints are tenant-scoped from the request context, never from the body — the question the
/// operator types has no say over which organization's bank is read.
/// </summary>
public static class DiscoveryBankEndpointsExtensions
{
    public static IEndpointRouteBuilder MapDiscoveryBankEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/discovery-bank")
            .WithTags("Social Discovery Bank")
            .RequireAuthorization();

        group.MapGet("/", DiscoveryBankEndpointFacade.ListAsync)
            .WithName("ListDiscoveryBankCandidates")
            .WithSummary("List the organization's discovery candidate bank (paged, newest observation first)")
            .Produces<DiscoveryBankPageResponse>(StatusCodes.Status200OK);

        group.MapPost("/ask", DiscoveryBankEndpointFacade.AskAsync)
            .WithName("AskDiscoveryBankQuestion")
            .WithSummary("Answer a question about the discovery candidate bank with a deterministic query plus an AI narration")
            .Produces<DiscoveryBankAnswerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}

/// <summary>
/// Internal facade exposing the handlers as static methods so tests can call them without a host.
/// </summary>
internal static class DiscoveryBankEndpointFacade
{
    internal static async Task<IResult> ListAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return DiscoveryBankErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new ListDiscoveryBankQuery(tenantId.Value, page ?? 1, pageSize ?? 50),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> AskAsync(
        AskDiscoveryBankQuestionRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return DiscoveryBankErrors.OrganizationRequired.ToProblem();
        }

        var history = body.History?
            .Where(turn => turn is not null && !string.IsNullOrWhiteSpace(turn.Text))
            .Select(turn => new DiscoveryBankChatTurn(turn.Role ?? "user", turn.Text))
            .ToList() ?? [];

        var result = await sender.Send(
            new AskDiscoveryBankQuestionQuery(tenantId.Value, body.Question ?? string.Empty, history),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }
}

public sealed record AskDiscoveryBankQuestionRequest(
    string? Question,
    IReadOnlyList<AskDiscoveryBankHistoryTurn>? History);

public sealed record AskDiscoveryBankHistoryTurn(string? Role, string Text);
