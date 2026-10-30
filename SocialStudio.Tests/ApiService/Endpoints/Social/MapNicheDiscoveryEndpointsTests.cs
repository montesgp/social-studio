using SocialStudio.ApiService.Endpoints.Social;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.NicheDiscovery;
using SocialStudio.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Tests.ApiService.Endpoints.Social;

/// <summary>
/// WU-11b (design §10): the 5 routes map to the correct handlers and status codes, including
/// <c>GET /markets</c> (ND-14 — the market selector is data-driven by construction).
/// </summary>
public sealed class MapNicheDiscoveryEndpointsTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void MapNicheDiscoveryEndpoints_RegistersEveryRoute_WithExpectedMethods()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        // These types only need to be resolvable so RequestDelegateFactory can infer them as
        // [FromServices] parameters — this test inspects registered routes, it never invokes them.
        builder.Services.AddSingleton<ISender>(new FakeSender(Result.Success(new StartDiscoveryRunResponse(Guid.Empty, "Requested"))));
        builder.Services.AddSingleton<ICurrentTenant>(new FakeTenant(Org));
        builder.Services.AddSingleton<ICurrentUser>(new FakeUser(User));
        builder.Services.AddSingleton<IMarketCatalogRepository>(new FakeMarketCatalog());
        var app = builder.Build();

        app.MapNicheDiscoveryEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText != null && e.RoutePattern.RawText.StartsWith("/api/social/niche-discovery", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(6, endpoints.Count);
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/api/social/niche-discovery/runs" && HasMethod(e, "POST"));

        // The organization's run history: without it a completed run is only reachable while the browser
        // tab stays open.
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/api/social/niche-discovery/runs" && HasMethod(e, "GET"));
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/api/social/niche-discovery/runs/{runId:guid}" && HasMethod(e, "GET"));
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/api/social/niche-discovery/runs/{runId:guid}/opportunities" && HasMethod(e, "GET"));
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/api/social/niche-discovery/opportunities/{opportunityId:guid}" && HasMethod(e, "GET"));
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/api/social/niche-discovery/markets" && HasMethod(e, "GET"));
    }

    [Fact]
    public async Task StartRunAsync_HappyPath_Returns202WithRunIdAndStatus()
    {
        var response = new StartDiscoveryRunResponse(Guid.NewGuid(), "Requested");
        var sender = new FakeSender(Result.Success(response));
        var body = new StartDiscoveryRunHttpRequest("es-AR", "pt-BR", "youtube", "video", null, null, null, ["budget travel"], null);

        var result = await NicheDiscoveryEndpointFacade.StartRunAsync(body, sender, new FakeTenant(Org), new FakeUser(User), CancellationToken.None);

        var accepted = Assert.IsType<Accepted<StartDiscoveryRunResponse>>(result);
        Assert.Equal(response, accepted.Value);
        var command = Assert.IsType<StartDiscoveryRunCommand>(sender.LastRequest);
        Assert.Equal(Org, command.OrganizationId);
        Assert.Equal(User, command.UserId);
    }

    [Fact]
    public async Task StartRunAsync_NoTenant_ReturnsProblem_AndNeverCallsTheSender()
    {
        var sender = new FakeSender(Result.Success(new StartDiscoveryRunResponse(Guid.NewGuid(), "Requested")));
        var body = new StartDiscoveryRunHttpRequest("es-AR", "pt-BR", "youtube", "video", null, null, null, [], null);

        var result = await NicheDiscoveryEndpointFacade.StartRunAsync(body, sender, new FakeTenant(null), new FakeUser(User), CancellationToken.None);

        Assert.IsNotType<Accepted<StartDiscoveryRunResponse>>(result);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task GetRunAsync_RunNotFound_ReturnsProblem()
    {
        var sender = new FakeSender(Result.Failure<DiscoveryRunStatusResponse>(NicheDiscoveryErrors.RunNotFound));

        var result = await NicheDiscoveryEndpointFacade.GetRunAsync(Guid.NewGuid(), sender, new FakeTenant(Org), CancellationToken.None);

        Assert.IsNotType<Ok<DiscoveryRunStatusResponse>>(result);
    }

    [Fact]
    public async Task ListMarketsAsync_ReturnsTheSeededCatalog_DataDrivenByConstruction()
    {
        var catalog = new FakeMarketCatalog();

        var result = await NicheDiscoveryEndpointFacade.ListMarketsAsync(catalog, CancellationToken.None);

        var ok = Assert.IsType<Ok<MarketCatalogResponse>>(result);
        Assert.Equal(2, ok.Value!.Markets.Count);
        Assert.Contains(ok.Value.Markets, m => m.Code == "es-AR");
        Assert.Contains(ok.Value.Markets, m => m.Code == "pt-BR");
    }

    private static bool HasMethod(RouteEndpoint endpoint, string method) =>
        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false;

    private sealed class FakeSender : ISender
    {
        private readonly object _response;

        public FakeSender(object response) => _response = response;

        public object? LastRequest { get; private set; }

        public Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResult)_response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResult> CreateStream<TResult>(IStreamRequest<TResult> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTenant : ICurrentTenant
    {
        public FakeTenant(Guid? orgId) => TenantId = orgId is { } id ? new TenantId(id) : null;

        public TenantId? TenantId { get; }

        public bool IsResolved => TenantId is not null;
    }

    private sealed class FakeUser : ICurrentUser
    {
        public FakeUser(Guid? userId) => UserId = userId;

        public bool IsAuthenticated => UserId is not null;

        public Guid? UserId { get; }

        public string? Email => null;

        public bool IsPlatformAdmin => false;
    }

    private sealed class FakeMarketCatalog : IMarketCatalogRepository
    {
        private readonly List<MarketCatalogEntry> _entries =
        [
            MarketCatalogEntry.Create(MarketCode.TryParse("es-AR").Value, "social.niche_discovery.markets.es_ar", "es", true).Value,
            MarketCatalogEntry.Create(MarketCode.TryParse("pt-BR").Value, "social.niche_discovery.markets.pt_br", "pt", true).Value,
        ];

        public Task<IReadOnlyList<MarketCatalogEntry>> ListActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MarketCatalogEntry>>(_entries);

        public Task<MarketCatalogEntry?> GetByCodeAsync(MarketCode code, CancellationToken ct = default) =>
            Task.FromResult(_entries.FirstOrDefault(e => e.Code.Value == code.Value));
    }
}
