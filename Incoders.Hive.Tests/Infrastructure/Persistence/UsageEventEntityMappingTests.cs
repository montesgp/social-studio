using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

public sealed class UsageEventEntityMappingTests
{
    private static readonly DateTime Now = new(2026, 5, 26, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UsageEventData_RoundTrip_AllNonNullableFieldsPersistCorrectly()
    {
        await using var db = CreateDbContext();
        var id = new UsageEventData
        {
            OrganizationId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            WorkflowKey = "single-analyzer",
            PipelineStepKey = "ai-analysis",
            ProviderKey = "claude",
            Model = "claude-sonnet-4-5",
            PromptVersion = "v1",
            RequestTimestamp = Now.AddSeconds(-2),
            CompletedTimestamp = Now,
            DurationMs = 2000,
            InputTokens = 500,
            OutputTokens = 200,
            EstimatedCostUsd = 0.001m,
            RequestSizeBytes = 1024,
            ResponseSizeBytes = 2048,
            RetryCount = 0,
            Status = "succeeded",
            FailureCode = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = Now,
        };

        db.UsageEvents.Add(id);
        await db.SaveChangesAsync();

        var read = await db.UsageEvents.FindAsync(id.Id);
        Assert.NotNull(read);
        Assert.Equal(id.OrganizationId, read!.OrganizationId);
        Assert.Equal(id.WorkflowKey, read.WorkflowKey);
        Assert.Equal(id.PipelineStepKey, read.PipelineStepKey);
        Assert.Equal(id.ProviderKey, read.ProviderKey);
        Assert.Equal(id.Model, read.Model);
        Assert.Equal(id.Status, read.Status);
        Assert.Equal(id.InputTokens, read.InputTokens);
        Assert.Equal(id.OutputTokens, read.OutputTokens);
        Assert.Equal(id.EstimatedCostUsd, read.EstimatedCostUsd);
        Assert.Equal(id.CorrelationId, read.CorrelationId);
        Assert.Equal(id.DurationMs, read.DurationMs);
    }

    [Fact]
    public async Task UsageEventData_NullableFields_AllowNull()
    {
        await using var db = CreateDbContext();
        var entity = new UsageEventData
        {
            OrganizationId = Guid.NewGuid(),
            ClientId = null,
            WorkflowKey = "single-analyzer",
            PipelineStepKey = "social-extraction",
            ProviderKey = "apify",
            Model = null,
            PromptVersion = null,
            RequestTimestamp = Now,
            CompletedTimestamp = Now.AddSeconds(3),
            DurationMs = 3000,
            InputTokens = null,
            OutputTokens = null,
            EstimatedCostUsd = null,
            RequestSizeBytes = null,
            ResponseSizeBytes = null,
            RetryCount = 1,
            Status = "tokens-missing",
            FailureCode = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = Now,
        };

        db.UsageEvents.Add(entity);
        await db.SaveChangesAsync();

        var read = await db.UsageEvents.FindAsync(entity.Id);
        Assert.NotNull(read);
        Assert.Null(read!.ClientId);
        Assert.Null(read.Model);
        Assert.Null(read.InputTokens);
        Assert.Null(read.EstimatedCostUsd);
        Assert.Equal(1, read.RetryCount);
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"usage-events-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
