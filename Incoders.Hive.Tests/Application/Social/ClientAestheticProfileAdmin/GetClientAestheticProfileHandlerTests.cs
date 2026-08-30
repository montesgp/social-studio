using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.GetProfile;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ClientAestheticProfileAdmin;

/// <summary>
/// client-analysis-data admin panel: GetClientAestheticProfileHandler resolves latest analysis
/// metadata + full decoded profile, degrading gracefully instead of throwing on missing/sparse
/// data.
/// </summary>
public sealed class GetClientAestheticProfileHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientGuid = Guid.NewGuid();

    [Fact]
    public async Task ClientNotFound_ReturnsFailure()
    {
        var handler = new GetClientAestheticProfileHandler(
            new FakeClientRepository(exists: false),
            new FakeClientRepository(exists: false),
            new FakeAnalysisRepository(null),
            new FakeProfileReader(null));

        var result = await handler.Handle(
            new GetClientAestheticProfileQuery(OrgId, ClientGuid), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task NoAnalysisExists_ReturnsNoAnalysisResponse_NotError()
    {
        var handler = new GetClientAestheticProfileHandler(
            new FakeClientRepository(exists: true),
            new FakeClientRepository(exists: true),
            new FakeAnalysisRepository(null),
            new FakeProfileReader(null));

        var result = await handler.Handle(
            new GetClientAestheticProfileQuery(OrgId, ClientGuid), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasAnalysis);
        Assert.Null(result.Value.Profile);
    }

    [Fact]
    public async Task LatestAnalysisInProgress_ReturnsStatus_WithNullProfile()
    {
        var analysisId = Guid.NewGuid();
        var summary = new LatestProfileAnalysisSummary(analysisId, "Analyzing", null, "@client", null);

        var handler = new GetClientAestheticProfileHandler(
            new FakeClientRepository(exists: true),
            new FakeClientRepository(exists: true),
            new FakeAnalysisRepository(summary),
            new FakeProfileReader(null));

        var result = await handler.Handle(
            new GetClientAestheticProfileQuery(OrgId, ClientGuid), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAnalysis);
        Assert.Equal("Analyzing", result.Value.Status);
        Assert.Null(result.Value.Profile);
    }

    [Fact]
    public async Task LatestAnalysisAnalyzed_ReturnsFullDecodedProfile()
    {
        var analysisId = Guid.NewGuid();
        var completedAt = DateTime.UtcNow;
        var summary = new LatestProfileAnalysisSummary(analysisId, "Analyzed", completedAt, "@client", """{"styleDescriptors":["airy"]}""");
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: ["gradient"],
            VoiceTone: "warm",
            AverageSlidesPerCarousel: 5,
            AnalyzedAtUtc: completedAt,
            BackgroundPattern: [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor]);

        var handler = new GetClientAestheticProfileHandler(
            new FakeClientRepository(exists: true),
            new FakeClientRepository(exists: true),
            new FakeAnalysisRepository(summary),
            new FakeProfileReader(profile));

        var result = await handler.Handle(
            new GetClientAestheticProfileQuery(OrgId, ClientGuid), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAnalysis);
        Assert.Equal(analysisId, result.Value.AnalysisId);
        Assert.NotNull(result.Value.Profile);
        Assert.Equal(2, result.Value.Profile!.BackgroundPattern!.Count);
    }

    [Fact]
    public async Task NullOrganization_ResolvesClientAcrossOrganizations_ForPlatformAdmin()
    {
        // The Analysis Data tab used to render "social.clients.errors.not_found" for any client
        // outside the admin's own tenant, because this lookup was tenant-scoped while the clients
        // list it was opened from was not.
        var analysisId = Guid.NewGuid();
        var summary = new LatestProfileAnalysisSummary(analysisId, "Analyzing", null, "@client", null);

        var handler = new GetClientAestheticProfileHandler(
            new FakeClientRepository(exists: false),
            new FakeClientRepository(exists: true),
            new FakeAnalysisRepository(summary),
            new FakeProfileReader(null));

        var result = await handler.Handle(
            new GetClientAestheticProfileQuery(null, ClientGuid), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasAnalysis);
        Assert.Equal(analysisId, result.Value.AnalysisId);
    }

    // ── Fakes ──────────────────────────────────────────────────────────────

    private sealed class FakeClientRepository : ISocialClientRepository, ISocialClientDirectoryRepository
    {
        private readonly bool _exists;

        public FakeClientRepository(bool exists) => _exists = exists;

        public Task<Client?> GetByIdAsync(ClientId id, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_exists ? Restore(id, organizationId) : null);

        public Task<Client?> GetByIdAcrossOrganizationsAsync(ClientId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_exists ? Restore(id, OrgId) : null);

        private static Client? Restore(ClientId id, Guid organizationId) =>
            Client.Restore(id, organizationId, "Test Client", null, true, DateTime.UtcNow, DateTime.UtcNow, null, null, null);

        public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(SocialClientQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<Client>, int)>(([], 0));

        public Task AddAsync(Client client, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(Client client) { }
    }

    private sealed class FakeAnalysisRepository : IProfileAnalysisRepository
    {
        private readonly LatestProfileAnalysisSummary? _summary;

        public FakeAnalysisRepository(LatestProfileAnalysisSummary? summary) => _summary = summary;

        public Task<LatestProfileAnalysisSummary?> GetLatestForClientAsync(Guid orgId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_summary);

        public Task AddAsync(ProfileAnalysis analysis, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ProfileAnalysis?> GetByIdAsync(Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProfileAnalysis?>(null);

        public Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileAnalysisHistoryPage([], 0, page, pageSize));

        public Task UpdateAsync(ProfileAnalysis analysis, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<DateTime?> GetLatestAnalyzedCompletedAtAsync(Guid orgId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateTime?>(null);

        public Task<bool> HasNonTerminalAnalysisForClientAsync(Guid orgId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeProfileReader : IClientAestheticProfileReader
    {
        private readonly ClientAestheticProfile? _profile;

        public FakeProfileReader(ClientAestheticProfile? profile) => _profile = profile;

        public Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
            Task.FromResult(_profile);
    }
}
