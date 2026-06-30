using Incoders.Hive.Application.Abstractions.Integrations;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// WU-6 [RED] 6.1: correlation contract for IntegrationExecutionAttemptRecord and IntegrationExecutionRequest.
/// AnalysisId is now nullable; CorrelationId/CorrelationKind are additive optional trailing fields.
/// Refs: CAC-INT-01; design §3.5, OQ-6.
/// </summary>
public sealed class IntegrationExecutionCorrelationTests
{
    // -----------------------------------------------------------------------
    // IntegrationExecutionAttemptRecord — AnalysisId nullable
    // -----------------------------------------------------------------------

    [Fact]
    public void AttemptRecord_AnalysisId_AcceptsNull()
    {
        // WHEN constructing a record with AnalysisId = null (content-generation, no analysis)
        var record = new IntegrationExecutionAttemptRecord(
            Id: Guid.NewGuid(),
            AnalysisId: null,
            CapabilityKey: "ai-image",
            ProviderKey: "openai",
            Status: IntegrationExecutionAttemptStatus.Succeeded,
            AttemptNumber: 1,
            FailureCode: null,
            RetryCount: 0,
            ElapsedMs: 50,
            DiagnosticsJson: null,
            StartedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow);

        // THEN AnalysisId is null
        Assert.Null(record.AnalysisId);
    }

    [Fact]
    public void AttemptRecord_AnalysisId_NonNullStillWorks()
    {
        // GIVEN an existing analysis-scoped construction site (must compile unchanged)
        var analysisId = Guid.NewGuid();
        var record = new IntegrationExecutionAttemptRecord(
            Id: Guid.NewGuid(),
            AnalysisId: analysisId,
            CapabilityKey: "ai-analysis",
            ProviderKey: "openai",
            Status: IntegrationExecutionAttemptStatus.Succeeded,
            AttemptNumber: 1,
            FailureCode: null,
            RetryCount: 0,
            ElapsedMs: 100,
            DiagnosticsJson: null,
            StartedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow);

        // THEN existing construction is unchanged
        Assert.Equal(analysisId, record.AnalysisId);
    }

    // -----------------------------------------------------------------------
    // IntegrationExecutionAttemptRecord — CorrelationId/CorrelationKind trailing fields
    // -----------------------------------------------------------------------

    [Fact]
    public void AttemptRecord_CorrelationId_DefaultsToNull()
    {
        var record = new IntegrationExecutionAttemptRecord(
            Id: Guid.NewGuid(),
            AnalysisId: Guid.NewGuid(),
            CapabilityKey: "ai-analysis",
            ProviderKey: "openai",
            Status: IntegrationExecutionAttemptStatus.Succeeded,
            AttemptNumber: 1,
            FailureCode: null,
            RetryCount: 0,
            ElapsedMs: 100,
            DiagnosticsJson: null,
            StartedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow);

        Assert.Null(record.CorrelationId);
        Assert.Null(record.CorrelationKind);
    }

    [Fact]
    public void AttemptRecord_CorrelationId_AcceptsNonNull()
    {
        var correlationId = Guid.NewGuid();

        var record = new IntegrationExecutionAttemptRecord(
            Id: Guid.NewGuid(),
            AnalysisId: null,
            CapabilityKey: "ai-image",
            ProviderKey: "openai",
            Status: IntegrationExecutionAttemptStatus.Succeeded,
            AttemptNumber: 1,
            FailureCode: null,
            RetryCount: 0,
            ElapsedMs: 50,
            DiagnosticsJson: null,
            StartedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow,
            CorrelationId: correlationId,
            CorrelationKind: "content-generation/generate-image");

        Assert.Equal(correlationId, record.CorrelationId);
        Assert.Equal("content-generation/generate-image", record.CorrelationKind);
    }

    // -----------------------------------------------------------------------
    // IntegrationExecutionRequest — CorrelationId/CorrelationKind trailing fields
    // -----------------------------------------------------------------------

    [Fact]
    public void ExecutionRequest_CorrelationId_DefaultsToNull()
    {
        var request = new IntegrationExecutionRequest(
            OrganizationId: Guid.NewGuid(),
            AnalysisId: Guid.NewGuid(),
            FlowKey: "single-analyzer",
            CapabilityKey: "ai-analysis",
            PlatformKey: null);

        Assert.Null(request.CorrelationId);
        Assert.Null(request.CorrelationKind);
    }

    [Fact]
    public void ExecutionRequest_CorrelationId_AcceptsNonNull()
    {
        var correlationId = Guid.NewGuid();

        var request = new IntegrationExecutionRequest(
            OrganizationId: Guid.NewGuid(),
            AnalysisId: null,
            FlowKey: "single-analyzer",
            CapabilityKey: "ai-image",
            PlatformKey: null,
            CorrelationId: correlationId,
            CorrelationKind: "content-generation/generate-image");

        Assert.Equal(correlationId, request.CorrelationId);
        Assert.Equal("content-generation/generate-image", request.CorrelationKind);
    }
}
