using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.UpdateBackgroundPattern;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ClientAestheticProfileAdmin;

/// <summary>
/// client-analysis-data admin panel: UpdateClientAestheticProfileBackgroundPatternHandler patches
/// only BackgroundPattern on the latest Analyzed analysis, round-tripping every other stored
/// aesthetic field untouched.
/// </summary>
public sealed class UpdateClientAestheticProfileBackgroundPatternHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientGuid = Guid.NewGuid();

    [Fact]
    public async Task ClientNotFound_ReturnsFailure()
    {
        var handler = new UpdateClientAestheticProfileBackgroundPatternHandler(
            new FakeClientRepository(exists: false),
            new FakeAnalysisRepository(null));

        var result = await handler.Handle(
            new UpdateClientAestheticProfileBackgroundPatternCommand(OrgId, ClientGuid, ["Photo"]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task NoAnalysis_ReturnsNoCompletedAnalysisError()
    {
        var handler = new UpdateClientAestheticProfileBackgroundPatternHandler(
            new FakeClientRepository(exists: true),
            new FakeAnalysisRepository(null));

        var result = await handler.Handle(
            new UpdateClientAestheticProfileBackgroundPatternCommand(OrgId, ClientGuid, ["Photo"]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProfileAnalysisErrors.NoCompletedAnalysis, result.Error);
    }

    [Fact]
    public async Task LatestAnalysisNotAnalyzed_ReturnsNoCompletedAnalysisError()
    {
        var summary = new LatestProfileAnalysisSummary(Guid.NewGuid(), "Analyzing", null, "@client", null);
        var handler = new UpdateClientAestheticProfileBackgroundPatternHandler(
            new FakeClientRepository(exists: true),
            new FakeAnalysisRepository(summary));

        var result = await handler.Handle(
            new UpdateClientAestheticProfileBackgroundPatternCommand(OrgId, ClientGuid, ["Photo"]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProfileAnalysisErrors.NoCompletedAnalysis, result.Error);
    }

    [Fact]
    public async Task Analyzed_PatchesBackgroundPattern_PreservesOtherFields()
    {
        var analysisId = Guid.NewGuid();
        var extraction = new AestheticExtraction(
            Palette: [new AestheticPaletteEntry("#ABCDEF", "Primary", 1)],
            StyleDescriptors: ["editorial"],
            Motifs: ["arrows"],
            CaptionVoice: "playful",
            BackgroundPattern: [SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor]);
        var existingJson = AestheticProfilePersistence.Serialize(extraction, []);
        var summary = new LatestProfileAnalysisSummary(analysisId, "Analyzed", DateTime.UtcNow, "@client", existingJson);

        var analysisRepo = new FakeAnalysisRepository(summary);
        var handler = new UpdateClientAestheticProfileBackgroundPatternHandler(
            new FakeClientRepository(exists: true),
            analysisRepo);

        var result = await handler.Handle(
            new UpdateClientAestheticProfileBackgroundPatternCommand(
                OrgId, ClientGuid, ["Photo", "FlatColor", "FlatColor", "FlatColor", "Photo"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(analysisId, result.Value.AnalysisId);
        Assert.Equal(5, result.Value.TypicalSlideCount);
        Assert.Equal(analysisId, analysisRepo.LastSetAnalysisId);

        var read = AestheticProfilePersistence.Read(analysisRepo.LastSetJson);
        Assert.Equal(SlideBackgroundKind.Photo, read.BackgroundPattern![0]);
        Assert.Equal(SlideBackgroundKind.Photo, read.BackgroundPattern[4]);
        Assert.Equal("editorial", read.StyleDescriptors?[0]);
        Assert.Equal("playful", read.CaptionVoice);
        Assert.Equal("#ABCDEF", read.Palette![0].Hex);
    }

    // ── Fakes ──────────────────────────────────────────────────────────────

    private sealed class FakeClientRepository : ISocialClientRepository
    {
        private readonly bool _exists;

        public FakeClientRepository(bool exists) => _exists = exists;

        public Task<Client?> GetByIdAsync(ClientId id, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_exists
                ? Client.Restore(id, organizationId, "Test Client", null, true, DateTime.UtcNow, DateTime.UtcNow, null, null, null)
                : null);

        public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(SocialClientQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<Client>, int)>(([], 0));

        public Task AddAsync(Client client, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(Client client) { }
    }

    private sealed class FakeAnalysisRepository : IProfileAnalysisRepository
    {
        private readonly LatestProfileAnalysisSummary? _summary;

        public Guid? LastSetAnalysisId { get; private set; }
        public string? LastSetJson { get; private set; }

        public FakeAnalysisRepository(LatestProfileAnalysisSummary? summary) => _summary = summary;

        public Task<LatestProfileAnalysisSummary?> GetLatestForClientAsync(Guid orgId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_summary);

        public Task SetAestheticProfileJsonAsync(Guid analysisId, Guid organizationId, string aestheticProfileJson, CancellationToken cancellationToken = default)
        {
            LastSetAnalysisId = analysisId;
            LastSetJson = aestheticProfileJson;
            return Task.CompletedTask;
        }

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
}
