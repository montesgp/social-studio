using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Export;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Export;

/// <summary>
/// B3 WU-3 TDD tests for <see cref="ExportFormatToPptxQueryHandler"/>.
///
/// <para>
/// RED: all tests fail before the handler is implemented.
/// GREEN: pass after <c>ExportFormatToPptxQueryHandler</c> is created.
/// </para>
///
/// <para>
/// Spec coverage: PPTX-02-a (editable download), PPTX-02-d (no markup → handled error),
/// PPTX-04 (single read path via persisted spec).
/// </para>
/// </summary>
public sealed class ExportFormatToPptxQueryHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    // ─── Happy path: envelope present → adapter called → bytes returned ──────

    [Fact]
    public async Task Handle_ValidEnvelope_DelegatesToAdapterAndReturnsPptxBytes()
    {
        var envelope = StubEnvelope();
        var specJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var expectedBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // PPTX magic

        var job = MakeJob(JobId, OrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", specJson);

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var adapter = new FakePptxAdapter(Result.Success(expectedBytes));

        var handler = new ExportFormatToPptxQueryHandler(jobRepo, formatResultRepo, adapter);
        var query = new ExportFormatToPptxQuery(JobId, "carousel", OrgId);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedBytes, result.Value.PptxBytes);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.DownloadFilename));
        Assert.True(adapter.WasCalled);
    }

    // ─── No markup: null CompositionSpecJson → handled not-exportable ────────

    [Fact]
    public async Task Handle_NullCompositionSpecJson_ReturnsNotExportableError()
    {
        var job = MakeJob(JobId, OrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", compositionSpecJson: null);

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var adapter = new FakePptxAdapter(Result.Success<byte[]>([]));

        var handler = new ExportFormatToPptxQueryHandler(jobRepo, formatResultRepo, adapter);
        var query = new ExportFormatToPptxQuery(JobId, "carousel", OrgId);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("content-gen.not-exportable", result.Error.Code);
        Assert.False(adapter.WasCalled, "Adapter MUST NOT be called when markup is absent.");
    }

    // ─── Format key not found in job → not-found ─────────────────────────────

    [Fact]
    public async Task Handle_FormatKeyNotFound_ReturnsNotFoundError()
    {
        var job = MakeJob(JobId, OrgId);
        // Format result is for "carousel", not "story"
        var formatResult = MakeFormatResult(JobId, "carousel", JsonSerializer.Serialize(StubEnvelope(), JsonOptions));

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);
        var adapter = new FakePptxAdapter(Result.Success<byte[]>([]));

        var handler = new ExportFormatToPptxQueryHandler(jobRepo, formatResultRepo, adapter);
        var query = new ExportFormatToPptxQuery(JobId, "story", OrgId); // querying "story"

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── Job not found / cross-tenant → not-found ────────────────────────────

    [Fact]
    public async Task Handle_JobNotFound_ReturnsNotFoundError()
    {
        var jobRepo = new FakeJobRepo(null); // no job
        var formatResultRepo = new FakeFormatResultRepo(null);
        var adapter = new FakePptxAdapter(Result.Success<byte[]>([]));

        var handler = new ExportFormatToPptxQueryHandler(jobRepo, formatResultRepo, adapter);
        var query = new ExportFormatToPptxQuery(Guid.NewGuid(), "carousel", OrgId);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── Adapter failure → handler propagates the error ──────────────────────

    [Fact]
    public async Task Handle_AdapterFailure_PropagatesError()
    {
        var envelope = StubEnvelope();
        var specJson = JsonSerializer.Serialize(envelope, JsonOptions);

        var job = MakeJob(JobId, OrgId);
        var formatResult = MakeFormatResult(JobId, "carousel", specJson);

        var adapterError = Error.Failure("pptx.build-failed", "PPTX build failed.");
        var adapter = new FakePptxAdapter(Result.Failure<byte[]>(adapterError));

        var jobRepo = new FakeJobRepo(job);
        var formatResultRepo = new FakeFormatResultRepo(formatResult);

        var handler = new ExportFormatToPptxQueryHandler(jobRepo, formatResultRepo, adapter);
        var query = new ExportFormatToPptxQuery(JobId, "carousel", OrgId);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("pptx.build-failed", result.Error.Code);
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

file sealed class FakePptxAdapter : IPptxExportAdapter
{
    private readonly Result<byte[]> _response;
    public bool WasCalled { get; private set; }

    public FakePptxAdapter(Result<byte[]> response) => _response = response;

    public Task<Result<byte[]>> BuildAsync(CompositionSpecEnvelope envelope, CancellationToken ct)
        => BuildManyAsync([envelope], ct);

    public Task<Result<byte[]>> BuildManyAsync(IReadOnlyList<CompositionSpecEnvelope> envelopes, CancellationToken ct)
    {
        WasCalled = true;
        return Task.FromResult(_response);
    }
}
