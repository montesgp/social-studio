using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Integrations;

/// <summary>
/// Entity-mapping tests for <see cref="IntegrationExecutionAttemptData"/> that cover the
/// schema changes introduced by migration
/// <c>20260629022233_RelaxAnalysisIdNullableAndAddCorrelationToIntegrationAttempts</c>:
/// <list type="bullet">
///   <item><c>analysis_id</c> made nullable (optional FK).</item>
///   <item><c>correlation_id</c> uuid column added (nullable).</item>
///   <item><c>correlation_kind</c> text column added (nullable).</item>
/// </list>
/// </summary>
public sealed class IntegrationExecutionAttemptMigrationTests
{
    // 6.5 RED → 6.6 GREEN: entity model reflects nullable AnalysisId.
    [Fact]
    public async Task AttemptData_NullAnalysisId_PersistsAndReadsBack()
    {
        await using var db = CreateDbContext();
        var correlationId = Guid.NewGuid();

        var entity = BuildAttempt(analysisId: null, correlationId: correlationId, correlationKind: "content-generation/generate-image");
        db.IntegrationExecutionAttempts.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.IntegrationExecutionAttempts.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Null(read!.AnalysisId);
        Assert.Equal(correlationId, read.CorrelationId);
        Assert.Equal("content-generation/generate-image", read.CorrelationKind);
    }

    // 6.5 RED → 6.6 GREEN: existing rows with non-null AnalysisId are unaffected.
    [Fact]
    public async Task AttemptData_NonNullAnalysisId_PersistsAndReadsBack()
    {
        await using var db = CreateDbContext();
        var analysisId = Guid.NewGuid();

        var entity = BuildAttempt(analysisId: analysisId, correlationId: null, correlationKind: null);
        db.IntegrationExecutionAttempts.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.IntegrationExecutionAttempts.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Equal(analysisId, read!.AnalysisId);
        Assert.Null(read.CorrelationId);
        Assert.Null(read.CorrelationKind);
    }

    // 6.5 RED → 6.6 GREEN: both anchors null is allowed by the entity model.
    [Fact]
    public async Task AttemptData_BothAnchorsNull_PersistsAndReadsBack()
    {
        await using var db = CreateDbContext();

        var entity = BuildAttempt(analysisId: null, correlationId: null, correlationKind: null);
        db.IntegrationExecutionAttempts.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.IntegrationExecutionAttempts.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Null(read!.AnalysisId);
        Assert.Null(read.CorrelationId);
        Assert.Null(read.CorrelationKind);
    }

    // 6.5 RED → 6.6 GREEN: repository record round-trip preserves correlation fields.
    [Fact]
    public async Task AttemptRecord_CorrelationFields_RoundTripViaRepository()
    {
        await using var db = CreateDbContext();
        var repo = new Incoders.Hive.Infrastructure.Persistence.Postgres.EfIntegrationExecutionAttemptRepository(db);

        var correlationId = Guid.NewGuid();
        var record = new IntegrationExecutionAttemptRecord(
            Id: Guid.NewGuid(),
            AnalysisId: null,
            CapabilityKey: "generate-image",
            ProviderKey: "carousel-background-service",
            Status: IntegrationExecutionAttemptStatus.Succeeded,
            AttemptNumber: 1,
            FailureCode: null,
            RetryCount: 0,
            ElapsedMs: 42,
            DiagnosticsJson: "{\"slideIndex\":3}",
            StartedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow,
            CorrelationId: correlationId,
            CorrelationKind: "content-generation/generate-image");

        await repo.AddAsync(record);
        await db.SaveChangesAsync();

        var read = await db.IntegrationExecutionAttempts.FindAsync(record.Id);
        Assert.NotNull(read);
        Assert.Equal(correlationId, read!.CorrelationId);
        Assert.Equal("content-generation/generate-image", read.CorrelationKind);
        Assert.Equal("generate-image", read.CapabilityKey);
        Assert.Equal(IntegrationExecutionAttemptStatus.Succeeded.ToString(), read.Status);
    }

    private static IntegrationExecutionAttemptData BuildAttempt(
        Guid? analysisId,
        Guid? correlationId,
        string? correlationKind) => new()
    {
        Id = Guid.NewGuid(),
        AnalysisId = analysisId,
        CapabilityKey = "generate-image",
        ProviderKey = "carousel-background-service",
        Status = "Succeeded",
        AttemptNumber = 1,
        RetryCount = 0,
        ElapsedMs = 10,
        StartedAtUtc = DateTime.UtcNow,
        CorrelationId = correlationId,
        CorrelationKind = correlationKind,
    };

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"integration-attempt-migration-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
