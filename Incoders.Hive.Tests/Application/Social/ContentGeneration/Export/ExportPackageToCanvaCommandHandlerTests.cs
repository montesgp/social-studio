using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Export;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Export;

/// <summary>
/// Tests for <see cref="ExportPackageToCanvaCommandHandler"/> (B3 — package-level Send to Canva).
/// The handler aggregates every VISUAL format of a job (flyer + each carousel slide) into ONE
/// multi-page PPTX, then imports it to Canva as a single editable design titled after the client.
/// </summary>
public sealed class ExportPackageToCanvaCommandHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OrgIntegrationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    [Fact]
    public async Task Handle_AggregatesFlyerAndAllCarouselSlides_IntoOneMultiPageImport()
    {
        // flyer = 1 editable envelope; carousel = 2 (one per slide) → 3 slides total.
        var flyerJson = CompositionSpecJson.Serialize([Env("<div data-slot=\"h\">Flyer</div>")]);
        var carouselJson = CompositionSpecJson.Serialize(
            [Env("<div data-slot=\"h\">Slide 1</div>"), Env("<div data-slot=\"h\">Slide 2</div>")]);

        var formatResults = new List<ContentGenerationFormatResult>
        {
            MakeFormatResult("carousel", carouselJson),
            MakeFormatResult("flyer", flyerJson),
            MakeFormatResult("reel-script", null), // text → no composition → excluded
        };

        var pptx = new CapturingPptxAdapter(Result.Success(new byte[] { 0x50, 0x4B }));
        var import = new RecordingImportClient(
            Result.Success(new CanvaImportResult("d-1", "https://canva.com/design/d-1/edit", DateTimeOffset.UtcNow.AddDays(30))));

        var handler = new ExportPackageToCanvaCommandHandler(
            new PkgJobRepo(MakeJob(OrgId)),
            new PkgFormatResultRepo(formatResults),
            new PkgTokenRepo(connected: true),
            new PkgOAuthService("access-token"),
            pptx,
            import,
            new PkgClientRepo());

        var result = await handler.Handle(
            new ExportPackageToCanvaCommand(JobId, OrgId, OrgIntegrationId, UserId), CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Code : "n/a")}");
        Assert.Equal("https://canva.com/design/d-1/edit", result.Value.EditUrl);
        Assert.True(import.WasCalled, "ImportAsync must be called.");
        // flyer (1) + carousel (2) = 3 editable pages; reel-script excluded.
        Assert.Equal(3, pptx.LastEnvelopeCount);
    }

    [Fact]
    public async Task Handle_NoVisualFormats_ReturnsNotExportable_AndDoesNotImport()
    {
        var formatResults = new List<ContentGenerationFormatResult>
        {
            MakeFormatResult("reel-script", null), // only a text format
        };

        var import = new RecordingImportClient(
            Result.Success(new CanvaImportResult("d", "https://x", DateTimeOffset.UtcNow)));

        var handler = new ExportPackageToCanvaCommandHandler(
            new PkgJobRepo(MakeJob(OrgId)),
            new PkgFormatResultRepo(formatResults),
            new PkgTokenRepo(connected: true),
            new PkgOAuthService("access-token"),
            new CapturingPptxAdapter(Result.Success(new byte[] { 0x50 })),
            import,
            new PkgClientRepo());

        var result = await handler.Handle(
            new ExportPackageToCanvaCommand(JobId, OrgId, OrgIntegrationId, UserId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("content-gen.not-exportable", result.Error.Code);
        Assert.False(import.WasCalled);
    }

    [Fact]
    public async Task Handle_UserNotConnected_ReturnsNotConnected_AndDoesNotImport()
    {
        var flyerJson = CompositionSpecJson.Serialize([Env("<div data-slot=\"h\">Flyer</div>")]);
        var formatResults = new List<ContentGenerationFormatResult> { MakeFormatResult("flyer", flyerJson) };

        var import = new RecordingImportClient(
            Result.Success(new CanvaImportResult("d", "https://x", DateTimeOffset.UtcNow)));

        var handler = new ExportPackageToCanvaCommandHandler(
            new PkgJobRepo(MakeJob(OrgId)),
            new PkgFormatResultRepo(formatResults),
            new PkgTokenRepo(connected: false), // no per-user token
            new PkgOAuthService(null),
            new CapturingPptxAdapter(Result.Success(new byte[] { 0x50 })),
            import,
            new PkgClientRepo());

        var result = await handler.Handle(
            new ExportPackageToCanvaCommand(JobId, OrgId, OrgIntegrationId, UserId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("canva.not_connected", result.Error.Code);
        Assert.False(import.WasCalled);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static CompositionSpecEnvelope Env(string markup) => new(
        SchemaVersion: CompositionSpecEnvelope.CurrentSchemaVersion,
        Markup: markup,
        Canvas: new CanvasDimensions(1080, 1920, 96),
        SlotGeometry: [],
        BackgroundImageRef: null);

    private static ContentGenerationJob MakeJob(Guid orgId) => new(
        Id: JobId,
        OrganizationId: orgId,
        ClientId: Guid.NewGuid(),
        AnalysisId: Guid.NewGuid(),
        RequestedFormats: ["flyer", "carousel"],
        Status: "Completed",
        StatusReason: null,
        ErrorCode: null,
        BrandSnapshotJson: "{}",
        ManifestJson: "{}",
        CorrelationId: Guid.NewGuid(),
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: DateTime.UtcNow,
        StartedAt: null,
        CompletedAt: DateTime.UtcNow);

    private static ContentGenerationFormatResult MakeFormatResult(string formatKey, string? specJson) => new(
        Id: Guid.NewGuid(),
        JobId: JobId,
        FormatKey: formatKey,
        Status: "Generated",
        OutputJson: null,
        CompositionSpecJson: specJson,
        RenderedAssetUri: null,
        InferredFieldsJson: null,
        PromptVersion: "v1",
        ErrorCode: null,
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: DateTime.UtcNow);
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

file sealed class CapturingPptxAdapter(Result<byte[]> response) : IPptxExportAdapter
{
    public int LastEnvelopeCount { get; private set; }

    public Task<Result<byte[]>> BuildAsync(CompositionSpecEnvelope envelope, CancellationToken ct)
        => BuildManyAsync([envelope], ct);

    public Task<Result<byte[]>> BuildManyAsync(IReadOnlyList<CompositionSpecEnvelope> envelopes, CancellationToken ct)
    {
        LastEnvelopeCount = envelopes.Count;
        return Task.FromResult(response);
    }
}

file sealed class RecordingImportClient(Result<CanvaImportResult> response) : ICanvaDesignImportClient
{
    public bool WasCalled { get; private set; }
    public string? LastDesignTitle { get; private set; }

    public Task<Result<CanvaImportResult>> ImportAsync(
        byte[] pptxBytes, string accessToken, string? designTitle = null, CancellationToken ct = default)
    {
        WasCalled = true;
        LastDesignTitle = designTitle;
        return Task.FromResult(response);
    }
}

file sealed class PkgJobRepo(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) =>
        Task.FromResult(job.Id == id && job.OrganizationId == organizationId ? job : null);

    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
    public Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class PkgFormatResultRepo(IReadOnlyList<ContentGenerationFormatResult> results) : IContentGenerationFormatResultRepository
{
    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult(results);

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
        Task.FromResult(result);
}

file sealed class PkgTokenRepo(bool connected) : ICanvaUserTokenRepository
{
    public Task<CanvaUserToken?> GetAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct)
    {
        if (!connected) return Task.FromResult<CanvaUserToken?>(null);
        var token = CanvaUserToken.Create(
            CanvaUserTokenId.New(), userId, organizationIntegrationId,
            "enc-access", "enc-refresh", DateTime.UtcNow.AddHours(1), "design:content:read", DateTime.UtcNow);
        return Task.FromResult<CanvaUserToken?>(token);
    }

    public Task UpsertAsync(CanvaUserToken token, CancellationToken ct) => Task.CompletedTask;
    public Task DeleteAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct) => Task.CompletedTask;
}

file sealed class PkgOAuthService(string? accessToken) : ICanvaOAuthService
{
    public Task<Result<string>> BuildAuthorizeUrlAsync(Guid userId, Guid organizationIntegrationId, Guid organizationId, string redirectUri, CancellationToken ct)
        => Task.FromResult(Result.Success("https://www.canva.com/api/oauth/authorize"));

    public Task<Result> ExchangeCodeAsync(string code, string state, CancellationToken ct = default)
        => Task.FromResult(Result.Success());

    public Task<Result<string>> EnsureFreshAsync(Guid userId, Guid organizationIntegrationId, Guid organizationId, CancellationToken ct)
        => Task.FromResult(accessToken is null
            ? Result.Failure<string>(Error.NotFound("canva.oauth.token_not_found", "No token."))
            : Result.Success(accessToken));

    public Task RevokeAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct) => Task.CompletedTask;
}

file sealed class PkgClientRepo : ISocialClientRepository
{
    // Returns null — the handler treats a missing client as "no title" (best-effort naming).
    public Task<Client?> GetByIdAsync(ClientId id, Guid organizationId, CancellationToken ct) =>
        Task.FromResult<Client?>(null);

    public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(SocialClientQuery query, CancellationToken ct) =>
        Task.FromResult<(IReadOnlyList<Client>, int)>(([], 0));

    public Task AddAsync(Client client, CancellationToken ct) => Task.CompletedTask;
    public void Update(Client client) { }
}
