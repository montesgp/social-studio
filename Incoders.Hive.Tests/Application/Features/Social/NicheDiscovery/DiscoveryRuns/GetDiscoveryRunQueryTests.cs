using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>WU-11b (design §10): poll endpoint status projection.</summary>
public sealed class GetDiscoveryRunQueryTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_ExistingRun_ReturnsItsStatusProjection()
    {
        var run = CreateRun();
        var handler = new GetDiscoveryRunHandler(new FakeRunRepository(run));

        var result = await handler.Handle(new GetDiscoveryRunQuery(Org, run.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(run.Id.Value, result.Value.RunId);
        Assert.Equal("Requested", result.Value.Status);
        // WU-12 gap fix: the dashboard needs the run's target market and content format to label
        // and filter opportunities (ND-12) — neither was on the status projection before.
        Assert.Equal("pt-BR", result.Value.TargetMarketCode);
        Assert.Equal("video", result.Value.ContentFormat);
    }

    [Fact]
    public async Task Handle_UnknownRun_ReturnsRunNotFound()
    {
        var handler = new GetDiscoveryRunHandler(new FakeRunRepository(CreateRun()));

        var result = await handler.Handle(new GetDiscoveryRunQuery(Org, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.RunNotFound.Code, result.Error.Code);
    }

    private static DiscoveryRun CreateRun()
    {
        var result = DiscoveryRun.Create(
            Org, User, "es-AR", "pt-BR", "youtube", "video",
            DiscoveryWindow.Default(), DiscoveryDepth.Standard, "{}", nicheId: null, Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class FakeRunRepository : IDiscoveryRunRepository
    {
        private readonly DiscoveryRun _run;

        public FakeRunRepository(DiscoveryRun run) => _run = run;

        public Task AddAsync(DiscoveryRun run, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(DiscoveryRun run, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DiscoveryRun?> GetByIdAsync(Guid organizationId, DiscoveryRunId id, CancellationToken ct = default) =>
            Task.FromResult(id == _run.Id && organizationId == _run.OrganizationId ? _run : null);
    }
}
