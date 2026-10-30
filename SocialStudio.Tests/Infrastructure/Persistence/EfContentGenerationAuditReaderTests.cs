using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using SocialStudio.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// Change 3 (job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1 incident): integration tests (EF InMemory
/// provider, real LINQ-to-Entities WHERE translation) for
/// <see cref="EfContentGenerationAuditReader"/> — the per-job audit timeline read model.
/// </summary>
public sealed class EfContentGenerationAuditReaderTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _analysisId = Guid.NewGuid();
    private readonly Guid _jobId = Guid.NewGuid();
    private readonly Guid _formatResultId = Guid.NewGuid();

    [Fact]
    public async Task GetJobTimelineAsync_ReturnsStepsInChronologicalOrder_RegardlessOfInsertOrder()
    {
        await using var db = CreateDb();

        SeedJob(db, status: "Partial");
        SeedFormatResult(db, _formatResultId, status: "PartiallyRendered");

        // Insert out of chronological order — the reader must sort by StartedAtUtc, not insert order.
        var t0 = DateTime.UtcNow;
        SeedAttempt(db, "generate-image", "carousel-background-service", "Succeeded",
            startedAt: t0.AddSeconds(30), diagnosticsJson: """{"slideIndex":2,"outcome":"succeeded"}""");
        SeedAttempt(db, "ai-image", "comfyui", "Succeeded", startedAt: t0);
        SeedAttempt(db, "generate-image", "carousel-background-service", "Failed",
            startedAt: t0.AddSeconds(60),
            failureCode: "integrations.provider_timeout",
            diagnosticsJson: """{"slideIndex":3,"outcome":"degraded-to-css","reason":"integrations.provider_timeout"}""");
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        Assert.NotNull(timeline);
        Assert.Equal(3, timeline!.Steps.Count);
        Assert.True(timeline.Steps[0].OccurredAtUtc <= timeline.Steps[1].OccurredAtUtc);
        Assert.True(timeline.Steps[1].OccurredAtUtc <= timeline.Steps[2].OccurredAtUtc);
        Assert.Equal("image-generation", timeline.Steps[0].StepType);
        Assert.Equal("slide-outcome", timeline.Steps[1].StepType);
        Assert.Equal("slide-outcome", timeline.Steps[2].StepType);
    }

    [Fact]
    public async Task GetJobTimelineAsync_SlideOutcomeStep_ExposesSlideIndexAndFailureReason()
    {
        await using var db = CreateDb();

        SeedJob(db, status: "Partial");
        SeedFormatResult(db, _formatResultId, status: "PartiallyRendered");
        SeedAttempt(db, "generate-image", "carousel-background-service", "Failed",
            startedAt: DateTime.UtcNow,
            failureCode: "integrations.provider_timeout",
            diagnosticsJson: """{"slideIndex":3,"outcome":"degraded-to-css","reason":"integrations.provider_timeout"}""");
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        var slideStep = Assert.Single(timeline!.Steps);
        Assert.Equal(3, slideStep.SlideIndex);
        Assert.Equal("degraded-to-css", slideStep.SlideOutcome);
        Assert.Equal("integrations.provider_timeout", slideStep.SlideOutcomeReason);
        Assert.False(slideStep.Succeeded);
        Assert.Equal("integrations.provider_timeout", slideStep.FailureCode);
        Assert.Equal("story-carousel", slideStep.FormatKey);
    }

    [Fact]
    public async Task GetJobTimelineAsync_ResolvesFormatKeyServerSide_WithoutClientJoiningCorrelationId()
    {
        await using var db = CreateDb();

        SeedJob(db, status: "Completed");
        SeedFormatResult(db, _formatResultId, status: "Rendered", formatKey: "feed-carousel");
        SeedAttempt(db, "ai-image", "comfyui", "Succeeded", startedAt: DateTime.UtcNow);
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        var step = Assert.Single(timeline!.Steps);
        // The response already carries the resolved FormatKey — the client never has to
        // join correlation_id -> format_result_id -> job itself.
        Assert.Equal("feed-carousel", step.FormatKey);
        Assert.Equal(_formatResultId, step.FormatResultId);
    }

    [Fact]
    public async Task GetJobTimelineAsync_PartiallyRenderedFormat_MarksEndedWithoutPersistingEverything()
    {
        await using var db = CreateDb();

        SeedJob(db, status: "Partial", requestedFormats: ["story-carousel"]);
        SeedFormatResult(db, _formatResultId, status: "PartiallyRendered");
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        Assert.True(timeline!.EndedWithoutPersistingEverything);
    }

    [Fact]
    public async Task GetJobTimelineAsync_AllRequestedFormatsRendered_DoesNotMarkEndedWithoutPersistingEverything()
    {
        await using var db = CreateDb();

        SeedJob(db, status: "Completed", requestedFormats: ["story-carousel"]);
        SeedFormatResult(db, _formatResultId, status: "Rendered");
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        Assert.False(timeline!.EndedWithoutPersistingEverything);
    }

    [Fact]
    public async Task GetJobTimelineAsync_ExposesPersistedAssetsPerFormat()
    {
        await using var db = CreateDb();

        SeedJob(db, status: "Completed");
        SeedFormatResult(db, _formatResultId, status: "Rendered");
        SeedAsset(db, _formatResultId, assetIndex: 1, filename: "slide_1.png");
        SeedAsset(db, _formatResultId, assetIndex: 2, filename: "slide_2.png");
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        var format = Assert.Single(timeline!.Formats);
        Assert.Equal(2, format.Assets.Count);
        Assert.Equal(1, format.Assets[0].AssetIndex);
        Assert.Equal(2, format.Assets[1].AssetIndex);
    }

    // ── Change 4: usage-event merge (incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1) ─────────

    [Fact]
    public async Task GetJobTimelineAsync_UsageEventsForJobsCorrelationId_AppearAsUsageEventSteps()
    {
        await using var db = CreateDb();
        var correlationId = Guid.NewGuid();

        SeedJob(db, status: "Failed", correlationId: correlationId);
        SeedUsageEvent(db, correlationId, "format:carousel", "anthropic", DateTime.UtcNow, durationMs: 17739);
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        var step = Assert.Single(timeline!.Steps);
        Assert.Equal("usage-event", step.StepType);
        Assert.Equal("format:carousel", step.CapabilityKey);
        Assert.Equal("carousel", step.FormatKey);
        Assert.Equal(17739, step.ElapsedMs);
        Assert.True(step.Succeeded);
    }

    [Fact]
    public async Task GetJobTimelineAsync_UsageEventForAnotherJob_IsNotIncluded()
    {
        await using var db = CreateDb();
        var correlationId = Guid.NewGuid();
        var otherJobCorrelationId = Guid.NewGuid();

        SeedJob(db, status: "Completed", correlationId: correlationId);
        SeedUsageEvent(db, otherJobCorrelationId, "format:carousel", "anthropic", DateTime.UtcNow, durationMs: 1000);
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        Assert.Empty(timeline!.Steps);
    }

    [Fact]
    public async Task GetJobTimelineAsync_UsageEventsAndAttempts_InterleaveInChronologicalOrder()
    {
        await using var db = CreateDb();
        var correlationId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        SeedJob(db, status: "Failed", correlationId: correlationId);
        SeedFormatResult(db, _formatResultId, status: "AiFailed");
        // format:carousel (usage-event) succeeds first, then the comfyui attempt runs, then the job
        // dies — mirrors the incident job's reconstructed flow.
        SeedUsageEvent(db, correlationId, "format:carousel", "anthropic", t0, durationMs: 17739);
        SeedAttempt(db, "ai-image", "comfyui", "Succeeded", startedAt: t0.AddSeconds(20));
        SeedUsageEvent(db, correlationId, "designer:feed-carousel", "anthropic", t0.AddSeconds(40), durationMs: 5000);
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        Assert.Equal(3, timeline!.Steps.Count);
        Assert.Equal("format:carousel", timeline.Steps[0].CapabilityKey);
        Assert.Equal("image-generation", timeline.Steps[1].StepType);
        Assert.Equal("designer:feed-carousel", timeline.Steps[2].CapabilityKey);
    }

    [Fact]
    public async Task GetJobTimelineAsync_UsageEventOverlappingAnImageGenerationAttempt_IsDeduplicated()
    {
        // Change 4 double-counting rule: an image-background:{formatKey} usage event and an ai-image
        // comfyui attempt describing the SAME provider call (same provider, near-identical start time,
        // near-identical duration — ~394262ms vs ~394201ms in the incident data) must not both appear,
        // or an operator would be misled into thinking the work happened twice.
        await using var db = CreateDb();
        var correlationId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        SeedJob(db, status: "Partial", correlationId: correlationId);
        SeedFormatResult(db, _formatResultId, status: "PartiallyRendered");
        SeedAttempt(db, "ai-image", "comfyui", "Succeeded", startedAt: t0);
        SeedUsageEvent(db, correlationId, "image-background:feed-carousel", "comfyui", t0.AddMilliseconds(60), durationMs: 394262);
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        var step = Assert.Single(timeline!.Steps);
        Assert.Equal("image-generation", step.StepType);
    }

    [Fact]
    public async Task GetJobTimelineAsync_UsageEventFromDifferentProvider_IsKeptEvenWhenNearAnAttempt()
    {
        // The prompt-writer call inside the SAME image-background:{formatKey} scope (a different,
        // fast Anthropic call) has no counterpart in integration_execution_attempts — different
        // provider key than the comfyui attempt — and must NOT be dropped by the dedup rule.
        await using var db = CreateDb();
        var correlationId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        SeedJob(db, status: "Partial", correlationId: correlationId);
        SeedFormatResult(db, _formatResultId, status: "PartiallyRendered");
        SeedAttempt(db, "ai-image", "comfyui", "Succeeded", startedAt: t0);
        SeedUsageEvent(db, correlationId, "image-background:feed-carousel", "anthropic", t0.AddMilliseconds(30), durationMs: 10421);
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        Assert.Equal(2, timeline!.Steps.Count);
        Assert.Contains(timeline.Steps, s => s.StepType == "image-generation");
        Assert.Contains(timeline.Steps, s => s.StepType == "usage-event" && s.ProviderKey == "anthropic");
    }

    [Fact]
    public async Task GetJobTimelineAsync_ZeroFormatResults_StillSurfacesUsageEventSteps()
    {
        // The exact gap the owner reported: a job that dies while writing copy — BEFORE any
        // format_result row is ever persisted — previously showed an empty audit because the
        // attempts query is gated on format result ids. Usage events are not.
        await using var db = CreateDb();
        var correlationId = Guid.NewGuid();

        SeedJob(db, status: "Failed", correlationId: correlationId);
        SeedUsageEvent(db, correlationId, "ai-analysis", "anthropic", DateTime.UtcNow, durationMs: 4200, failed: true);
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        var step = Assert.Single(timeline!.Steps);
        Assert.Equal("usage-event", step.StepType);
        Assert.Null(step.FormatKey);
        Assert.False(step.Succeeded);
    }

    [Fact]
    public async Task GetJobTimelineAsync_UsageEventStep_ExposesModelAndTokenUsage()
    {
        await using var db = CreateDb();
        var correlationId = Guid.NewGuid();

        SeedJob(db, status: "Completed", correlationId: correlationId);
        SeedUsageEvent(db, correlationId, "format:carousel", "anthropic", DateTime.UtcNow, durationMs: 17739,
            model: "claude-sonnet", inputTokens: 1200, outputTokens: 800, estimatedCostUsd: 0.05m);
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(_jobId, CancellationToken.None);

        var step = Assert.Single(timeline!.Steps);
        Assert.Equal("claude-sonnet", step.Model);
        Assert.Equal(1200, step.InputTokens);
        Assert.Equal(800, step.OutputTokens);
        Assert.Equal(0.05m, step.EstimatedCostUsd);
    }

    [Fact]
    public async Task GetJobTimelineAsync_UnknownJob_ReturnsNull()
    {
        await using var db = CreateDb();

        var reader = new EfContentGenerationAuditReader(db);
        var timeline = await reader.GetJobTimelineAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(timeline);
    }

    [Fact]
    public async Task ListJobsAsync_ClampsPageSizeToHardCap()
    {
        await using var db = CreateDb();
        for (var i = 0; i < 5; i++)
        {
            SeedJob(db, status: "Completed", jobId: Guid.NewGuid());
        }
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var page = await reader.ListJobsAsync(page: 1, pageSize: 10_000, CancellationToken.None);

        Assert.True(page.PageSize <= 100);
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task ListJobsAsync_OrdersMostRecentFirst()
    {
        await using var db = CreateDb();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        SeedJob(db, status: "Completed", jobId: older, createdAt: DateTime.UtcNow.AddHours(-2));
        SeedJob(db, status: "Completed", jobId: newer, createdAt: DateTime.UtcNow);
        await db.SaveChangesAsync();

        var reader = new EfContentGenerationAuditReader(db);
        var page = await reader.ListJobsAsync(page: 1, pageSize: 25, CancellationToken.None);

        Assert.Equal(newer, page.Items[0].JobId);
        Assert.Equal(older, page.Items[1].JobId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-generation-audit-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }

    private void SeedJob(
        AppIdentityDbContext db,
        string status,
        Guid? jobId = null,
        IReadOnlyList<string>? requestedFormats = null,
        DateTime? createdAt = null,
        Guid? correlationId = null)
    {
        db.ContentGenerationJobs.Add(new ContentGenerationJobData
        {
            Id = jobId ?? _jobId,
            OrganizationId = _orgId,
            ClientId = _clientId,
            AnalysisId = _analysisId,
            RequestedFormats = (requestedFormats ?? ["story-carousel"]).ToList(),
            Status = status,
            BrandSnapshotJson = "{}",
            CorrelationId = correlationId ?? Guid.NewGuid(),
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            CompletedAt = DateTime.UtcNow,
        });
    }

    private static void SeedUsageEvent(
        AppIdentityDbContext db,
        Guid correlationId,
        string pipelineStepKey,
        string providerKey,
        DateTime requestTimestamp,
        int durationMs,
        string? model = null,
        int? inputTokens = null,
        int? outputTokens = null,
        decimal? estimatedCostUsd = null,
        bool failed = false)
    {
        db.UsageEvents.Add(new UsageEventData
        {
            OrganizationId = Guid.NewGuid(),
            ClientId = null,
            WorkflowKey = "content-generation",
            PipelineStepKey = pipelineStepKey,
            ProviderKey = providerKey,
            Model = model,
            PromptVersion = null,
            RequestTimestamp = requestTimestamp,
            CompletedTimestamp = requestTimestamp.AddMilliseconds(durationMs),
            DurationMs = durationMs,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCostUsd = estimatedCostUsd,
            RequestSizeBytes = null,
            ResponseSizeBytes = null,
            RetryCount = 0,
            Status = failed ? "failed" : "succeeded",
            FailureCode = failed ? "provider.error" : null,
            CorrelationId = correlationId,
            CreatedAtUtc = requestTimestamp,
        });
    }

    private void SeedFormatResult(
        AppIdentityDbContext db,
        Guid formatResultId,
        string status,
        string formatKey = "story-carousel")
    {
        db.ContentGenerationFormatResults.Add(new ContentGenerationFormatResultData
        {
            Id = formatResultId,
            JobId = _jobId,
            FormatKey = formatKey,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
    }

    private void SeedAttempt(
        AppIdentityDbContext db,
        string capabilityKey,
        string providerKey,
        string status,
        DateTime startedAt,
        string? failureCode = null,
        string? diagnosticsJson = null)
    {
        db.IntegrationExecutionAttempts.Add(new IntegrationExecutionAttemptData
        {
            Id = Guid.NewGuid(),
            AnalysisId = null,
            CorrelationId = _formatResultId,
            CorrelationKind = "content-generation/generate-image",
            CapabilityKey = capabilityKey,
            ProviderKey = providerKey,
            Status = status,
            AttemptNumber = 1,
            FailureCode = failureCode,
            RetryCount = 0,
            ElapsedMs = 100,
            DiagnosticsJson = diagnosticsJson,
            StartedAtUtc = startedAt,
            CompletedAtUtc = startedAt.AddMilliseconds(100),
        });
    }

    private void SeedAsset(AppIdentityDbContext db, Guid formatResultId, int assetIndex, string filename)
    {
        db.ContentGenerationFormatAssets.Add(new ContentGenerationFormatAssetData
        {
            Id = Guid.NewGuid(),
            FormatResultId = formatResultId,
            AssetIndex = assetIndex,
            StoragePath = $"org/client/analysis/{filename}",
            Filename = filename,
            MimeType = "image/png",
            SizeBytes = 1024,
            ContentHash = "hash",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
    }
}
