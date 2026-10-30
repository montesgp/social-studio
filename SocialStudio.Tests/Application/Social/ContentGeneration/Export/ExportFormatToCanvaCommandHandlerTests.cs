using System.Text.Json;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Export;
using SocialStudio.Application.Features.Social.ContentGeneration.GeneratePackage;
using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Export;

/// <summary>
/// B3 WU-7 TDD tests for <see cref="ExportFormatToCanvaCommandHandler"/>.
///
/// <para>
/// RED: all tests fail before the handler is implemented.
/// GREEN: pass after ExportFormatToCanvaCommandHandler, ExportFormatToCanvaCommand, and
///        ExportFormatToCanvaResponse are created.
/// </para>
///
/// <para>
/// Spec coverage:
/// CANVA-EXPORT-01 (edit URL via Design Import),
/// CANVA-EXPORT-02 (user not connected → needs-authorization),
/// CANVA-EXPORT-03 (API failures → handled errors),
/// CANVA-USER-03 (unauthorized → delete token + reconnect-required).
/// </para>
/// </summary>
public sealed class ExportFormatToCanvaCommandHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OrgIntegrationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ─── Happy path: connected + markup present → import called → edit URL returned ──

    [Fact]
    public async Task Handle_HappyPath_ReturnEditUrl()
    {
        var envelope = StubEnvelope();
        var specJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var job = MakeJob(JobId, OrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", specJson);

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var tokenRepo = new FakeCanvaTokenRepo(connectedToken: true);
        var oauthService = new FakeCanvaOAuthService("valid-access-token");
        var pptxAdapter = new FakePptxAdapterForCanva(Result.Success(new byte[] { 0x50, 0x4B }));
        var importClient = new FakeCanvaImportClient(
            Result.Success(new CanvaImportResult("design-abc", "https://canva.com/design/abc/edit", DateTimeOffset.UtcNow.AddDays(30))));

        var handler = new ExportFormatToCanvaCommandHandler(
            jobRepo, formatResultRepo, tokenRepo, oauthService, pptxAdapter, importClient);

        var command = new ExportFormatToCanvaCommand(
            JobId: JobId,
            FormatKey: "carousel",
            OrganizationId: OrgId,
            OrganizationIntegrationId: OrgIntegrationId,
            UserId: UserId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error.Code}");
        Assert.Equal("https://canva.com/design/abc/edit", result.Value.EditUrl);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.DesignId));
        Assert.True(importClient.WasCalled, "ImportAsync MUST be called on happy path.");
        Assert.True(oauthService.EnsureFreshWasCalled, "EnsureFreshAsync MUST be called.");
        // Assert that the REAL organizationId was passed to EnsureFreshAsync
        Assert.Equal(OrgId, oauthService.LastOrganizationIdPassedToEnsureFresh);
    }

    // ─── User not connected → needs-authorization, import never called ────────

    [Fact]
    public async Task Handle_UserNotConnected_ReturnsNeedsAuthorizationWithAuthorizeUrl()
    {
        var envelope = StubEnvelope();
        var specJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var job = MakeJob(JobId, OrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", specJson);

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var tokenRepo = new FakeCanvaTokenRepo(connectedToken: false); // no token
        var oauthService = new FakeCanvaOAuthService(null); // build authorize URL
        var pptxAdapter = new FakePptxAdapterForCanva(Result.Success(new byte[] { 0x50, 0x4B }));
        var importClient = new FakeCanvaImportClient(
            Result.Success(new CanvaImportResult("design-abc", "https://canva.com/edit", DateTimeOffset.UtcNow)));

        var handler = new ExportFormatToCanvaCommandHandler(
            jobRepo, formatResultRepo, tokenRepo, oauthService, pptxAdapter, importClient);

        var command = new ExportFormatToCanvaCommand(
            JobId: JobId,
            FormatKey: "carousel",
            OrganizationId: OrgId,
            OrganizationIntegrationId: OrgIntegrationId,
            UserId: UserId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("canva.not_connected", result.Error.Code);
        Assert.False(importClient.WasCalled, "ImportAsync MUST NOT be called when user is not connected.");
        Assert.False(pptxAdapter.WasCalled, "PptxAdapter MUST NOT be called when user is not connected.");
    }

    // ─── Connected + expired token → refreshes and exports ───────────────────

    [Fact]
    public async Task Handle_UserConnectedExpiredToken_RefreshesAndExports()
    {
        var envelope = StubEnvelope();
        var specJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var job = MakeJob(JobId, OrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", specJson);

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var tokenRepo = new FakeCanvaTokenRepo(connectedToken: true);
        var oauthService = new FakeCanvaOAuthService("refreshed-access-token");
        var pptxAdapter = new FakePptxAdapterForCanva(Result.Success(new byte[] { 0x50, 0x4B }));
        var importClient = new FakeCanvaImportClient(
            Result.Success(new CanvaImportResult("d-2", "https://canva.com/design/d-2/edit", DateTimeOffset.UtcNow.AddDays(30))));

        var handler = new ExportFormatToCanvaCommandHandler(
            jobRepo, formatResultRepo, tokenRepo, oauthService, pptxAdapter, importClient);

        var command = new ExportFormatToCanvaCommand(
            JobId: JobId,
            FormatKey: "carousel",
            OrganizationId: OrgId,
            OrganizationIntegrationId: OrgIntegrationId,
            UserId: UserId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("refreshed-access-token", importClient.LastAccessTokenUsed);
        Assert.True(importClient.WasCalled);
    }

    // ─── No markup → not-exportable, import never called ─────────────────────

    [Fact]
    public async Task Handle_NullCompositionSpecJson_ReturnsNotExportableError()
    {
        var job = MakeJob(JobId, OrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", compositionSpecJson: null);

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var tokenRepo = new FakeCanvaTokenRepo(connectedToken: true);
        var oauthService = new FakeCanvaOAuthService("valid-token");
        var pptxAdapter = new FakePptxAdapterForCanva(Result.Success(new byte[] { 0x50 }));
        var importClient = new FakeCanvaImportClient(
            Result.Success(new CanvaImportResult("d", "url", DateTimeOffset.UtcNow)));

        var handler = new ExportFormatToCanvaCommandHandler(
            jobRepo, formatResultRepo, tokenRepo, oauthService, pptxAdapter, importClient);

        var command = new ExportFormatToCanvaCommand(
            JobId: JobId,
            FormatKey: "carousel",
            OrganizationId: OrgId,
            OrganizationIntegrationId: OrgIntegrationId,
            UserId: UserId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("content-gen.not-exportable", result.Error.Code);
        Assert.False(importClient.WasCalled, "ImportAsync MUST NOT be called when markup is absent.");
    }

    // ─── Import client timeout → propagated as handled error ─────────────────

    [Fact]
    public async Task Handle_ImportClientTimeout_ReturnsHandledError()
    {
        var envelope = StubEnvelope();
        var specJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var job = MakeJob(JobId, OrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", specJson);

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var tokenRepo = new FakeCanvaTokenRepo(connectedToken: true);
        var oauthService = new FakeCanvaOAuthService("valid-token");
        var pptxAdapter = new FakePptxAdapterForCanva(Result.Success(new byte[] { 0x50, 0x4B }));
        var importClient = new FakeCanvaImportClient(
            Result.Failure<CanvaImportResult>(
                Error.Failure("canva.import.timeout", "Import job timed out.")));

        var handler = new ExportFormatToCanvaCommandHandler(
            jobRepo, formatResultRepo, tokenRepo, oauthService, pptxAdapter, importClient);

        var command = new ExportFormatToCanvaCommand(
            JobId: JobId,
            FormatKey: "carousel",
            OrganizationId: OrgId,
            OrganizationIntegrationId: OrgIntegrationId,
            UserId: UserId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("canva.import.timeout", result.Error.Code);
    }

    // ─── Import returns 401 → delete token + reconnect-required error ────────

    [Fact]
    public async Task Handle_ImportClientUnauthorized_ClearsTokensAndReturnsReconnectError()
    {
        var envelope = StubEnvelope();
        var specJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var job = MakeJob(JobId, OrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", specJson);

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var tokenRepo = new FakeCanvaTokenRepo(connectedToken: true);
        var oauthService = new FakeCanvaOAuthService("valid-token");
        var pptxAdapter = new FakePptxAdapterForCanva(Result.Success(new byte[] { 0x50, 0x4B }));
        var importClient = new FakeCanvaImportClient(
            Result.Failure<CanvaImportResult>(
                Error.Failure("canva.import.unauthorized", "Access token is invalid.")));

        var handler = new ExportFormatToCanvaCommandHandler(
            jobRepo, formatResultRepo, tokenRepo, oauthService, pptxAdapter, importClient);

        var command = new ExportFormatToCanvaCommand(
            JobId: JobId,
            FormatKey: "carousel",
            OrganizationId: OrgId,
            OrganizationIntegrationId: OrgIntegrationId,
            UserId: UserId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("canva.reconnect_required", result.Error.Code);
        Assert.True(oauthService.RevokeWasCalled, "RevokeAsync MUST be called to clear stale token.");
    }

    // ─── organizationId fix: REAL org id flows into EnsureFreshAsync ──────────

    [Fact]
    public async Task Handle_RealOrganizationId_FlowsIntoEnsureFreshAsync()
    {
        var specificOrgId = Guid.NewGuid(); // a specific org to trace
        var envelope = StubEnvelope();
        var specJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var job = MakeJob(JobId, specificOrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", specJson);

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var tokenRepo = new FakeCanvaTokenRepo(connectedToken: true);
        var oauthService = new FakeCanvaOAuthService("token");
        var pptxAdapter = new FakePptxAdapterForCanva(Result.Success(new byte[] { 0x50 }));
        var importClient = new FakeCanvaImportClient(
            Result.Success(new CanvaImportResult("d", "https://canva.com/edit", DateTimeOffset.UtcNow)));

        var handler = new ExportFormatToCanvaCommandHandler(
            jobRepo, formatResultRepo, tokenRepo, oauthService, pptxAdapter, importClient);

        var command = new ExportFormatToCanvaCommand(
            JobId: JobId,
            FormatKey: "carousel",
            OrganizationId: specificOrgId,
            OrganizationIntegrationId: OrgIntegrationId,
            UserId: UserId);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The REAL org ID must reach EnsureFreshAsync — not the organizationIntegrationId proxy
        Assert.Equal(specificOrgId, oauthService.LastOrganizationIdPassedToEnsureFresh);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static CompositionSpecEnvelope StubEnvelope() =>
        new(
            SchemaVersion: 1,
            Markup: "<div data-slot=\"headline\">Hello</div>",
            Canvas: new CanvasDimensions(1080, 1920, 96),
            SlotGeometry:
            [
                new SlotGeometryEntry("headline", 10, 10, 200, 50, "Arial", 24, "#ffffff", "center"),
            ]);

    private static ContentGenerationJob MakeJob(Guid id, Guid orgId) =>
        new(
            Id: id,
            OrganizationId: orgId,
            ClientId: null,
            AnalysisId: Guid.NewGuid(),
            RequestedFormats: ["carousel"],
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

    private static ContentGenerationFormatResult MakeFormatResult(
        Guid jobId,
        string formatKey,
        string? compositionSpecJson) =>
        new(
            Id: Guid.NewGuid(),
            JobId: jobId,
            FormatKey: formatKey,
            Status: "Generated",
            OutputJson: null,
            CompositionSpecJson: compositionSpecJson,
            RenderedAssetUri: null,
            InferredFieldsJson: null,
            PromptVersion: "v1",
            ErrorCode: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow);
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

file sealed class FakeJobRepo : IContentGenerationJobRepository
{
    private readonly ContentGenerationJob? _job;
    public FakeJobRepo(ContentGenerationJob? job) => _job = job;

    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct)
    {
        if (_job is null) return Task.FromResult<ContentGenerationJob?>(null);
        if (_job.Id == id && _job.OrganizationId == organizationId)
            return Task.FromResult<ContentGenerationJob?>(_job);
        return Task.FromResult<ContentGenerationJob?>(null);
    }

    public Task InsertAsync(ContentGenerationJob job, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<ContentGenerationJob?>(null);
    public Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationJob>>(new List<ContentGenerationJob>());
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(
        Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class FakeFormatResultRepo : IContentGenerationFormatResultRepository
{
    private readonly ContentGenerationFormatResult? _result;
    public FakeFormatResultRepo(ContentGenerationFormatResult? result) => _result = result;

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(
            _result is not null ? new[] { _result }.ToList() : new List<ContentGenerationFormatResult>());

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
        Task.FromResult(result);
}

file sealed class FakeCanvaTokenRepo : ICanvaUserTokenRepository
{
    private readonly bool _hasToken;
    public bool DeleteWasCalled { get; private set; }

    public FakeCanvaTokenRepo(bool connectedToken) => _hasToken = connectedToken;

    public Task<CanvaUserToken?> GetAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct)
    {
        if (!_hasToken) return Task.FromResult<CanvaUserToken?>(null);

        var token = CanvaUserToken.Create(
            CanvaUserTokenId.New(),
            userId,
            organizationIntegrationId,
            "enc-access",
            "enc-refresh",
            DateTime.UtcNow.AddHours(1),
            "design:content:read",
            DateTime.UtcNow);
        return Task.FromResult<CanvaUserToken?>(token);
    }

    public Task UpsertAsync(CanvaUserToken token, CancellationToken ct) => Task.CompletedTask;

    public Task DeleteAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct)
    {
        DeleteWasCalled = true;
        return Task.CompletedTask;
    }
}

file sealed class FakeCanvaOAuthService : ICanvaOAuthService
{
    private readonly string? _accessToken; // null = simulate "no token" (BuildAuthorizeUrl path)
    public bool EnsureFreshWasCalled { get; private set; }
    public bool RevokeWasCalled { get; private set; }
    public Guid LastOrganizationIdPassedToEnsureFresh { get; private set; }

    public FakeCanvaOAuthService(string? accessToken) => _accessToken = accessToken;

    public Task<Result<string>> BuildAuthorizeUrlAsync(
        Guid userId, Guid organizationIntegrationId, Guid organizationId, string redirectUri, CancellationToken ct)
        => Task.FromResult(Result.Success("https://www.canva.com/api/oauth/authorize?state=test"));

    public Task<Result> ExchangeCodeAsync(string code, string state, CancellationToken ct = default)
        => Task.FromResult(Result.Success());

    public Task<Result<string>> EnsureFreshAsync(
        Guid userId, Guid organizationIntegrationId, Guid organizationId, CancellationToken ct)
    {
        EnsureFreshWasCalled = true;
        LastOrganizationIdPassedToEnsureFresh = organizationId;

        if (_accessToken is null)
            return Task.FromResult(Result.Failure<string>(
                Error.NotFound("canva.oauth.token_not_found", "No token stored.")));

        return Task.FromResult(Result.Success(_accessToken));
    }

    public Task RevokeAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct)
    {
        RevokeWasCalled = true;
        return Task.CompletedTask;
    }
}

file sealed class FakePptxAdapterForCanva : IPptxExportAdapter
{
    private readonly Result<byte[]> _response;
    public bool WasCalled { get; private set; }

    public FakePptxAdapterForCanva(Result<byte[]> response) => _response = response;

    public Task<Result<byte[]>> BuildAsync(CompositionSpecEnvelope envelope, CancellationToken ct)
        => BuildManyAsync([envelope], ct);

    public Task<Result<byte[]>> BuildManyAsync(IReadOnlyList<CompositionSpecEnvelope> envelopes, CancellationToken ct)
    {
        WasCalled = true;
        return Task.FromResult(_response);
    }
}

file sealed class FakeCanvaImportClient : ICanvaDesignImportClient
{
    private readonly Result<CanvaImportResult> _response;
    public bool WasCalled { get; private set; }
    public string? LastAccessTokenUsed { get; private set; }

    public FakeCanvaImportClient(Result<CanvaImportResult> response) => _response = response;

    public string? LastDesignTitleUsed { get; private set; }

    public Task<Result<CanvaImportResult>> ImportAsync(
        byte[] pptxBytes, string accessToken, string? designTitle = null, CancellationToken ct = default)
    {
        WasCalled = true;
        LastAccessTokenUsed = accessToken;
        LastDesignTitleUsed = designTitle;
        return Task.FromResult(_response);
    }
}
