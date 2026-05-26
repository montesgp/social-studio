using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Observability;

/// <summary>
/// Contract tests for IUsageEventLedger: fire-and-safe-log semantics.
/// These tests drive the shape of UsageEvent and IUsageEventLedger.
/// The throw-safety contract is exercised by the safe-ledger wrapper tests
/// in Infrastructure/Observability/UsageEventLedgerTests.cs (where the real
/// implementation wraps a throwing repository).
/// </summary>
public sealed class UsageEventLedgerContractTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    // -------------------------------------------------------------------
    // Happy path: successful AI call writes exactly one event
    // -------------------------------------------------------------------

    [Fact]
    public async Task RecordAsync_SuccessfulCall_WritesOneEventWithRequiredFields()
    {
        var capturedEvents = new List<UsageEvent>();
        var ledger = new CapturingUsageEventLedger(capturedEvents);

        var evt = BuildEvent(status: "succeeded", input: 500, output: 200);
        await ledger.RecordAsync(evt, CancellationToken.None);

        Assert.Single(capturedEvents);
        var saved = capturedEvents[0];

        Assert.Equal(OrgId, saved.OrganizationId);
        Assert.Equal("single-analyzer", saved.WorkflowKey);
        Assert.Equal("ai-analysis", saved.PipelineStepKey);
        Assert.Equal("claude", saved.ProviderKey);
        Assert.Equal("succeeded", saved.Status);
        Assert.Equal(500, saved.InputTokens);
        Assert.Equal(200, saved.OutputTokens);
        Assert.Equal(0, saved.RetryCount);
        Assert.NotEqual(Guid.Empty, saved.CorrelationId);
        Assert.True(saved.RequestTimestamp <= saved.CompletedTimestamp);
        Assert.True(saved.DurationMs >= 0);
    }

    // -------------------------------------------------------------------
    // Tokens-missing scenario
    // -------------------------------------------------------------------

    [Fact]
    public async Task RecordAsync_TokensMissing_EventWrittenWithNullTokenFieldsAndTokensMissingStatus()
    {
        var capturedEvents = new List<UsageEvent>();
        var ledger = new CapturingUsageEventLedger(capturedEvents);

        var evt = BuildEvent(status: "tokens-missing", input: null, output: null);
        await ledger.RecordAsync(evt, CancellationToken.None);

        Assert.Single(capturedEvents);
        var saved = capturedEvents[0];

        Assert.Equal("tokens-missing", saved.Status);
        Assert.Null(saved.InputTokens);
        Assert.Null(saved.OutputTokens);
    }

    // -------------------------------------------------------------------
    // UsageEvent shape: client_id is nullable
    // -------------------------------------------------------------------

    [Fact]
    public async Task RecordAsync_NullClientId_AcceptedWithoutError()
    {
        var capturedEvents = new List<UsageEvent>();
        var ledger = new CapturingUsageEventLedger(capturedEvents);

        var evt = BuildEvent("succeeded") with { ClientId = null };
        await ledger.RecordAsync(evt, CancellationToken.None);

        Assert.Single(capturedEvents);
        Assert.Null(capturedEvents[0].ClientId);
    }

    // -------------------------------------------------------------------
    // UsageEvent shape: estimated cost is nullable
    // -------------------------------------------------------------------

    [Fact]
    public async Task RecordAsync_NullEstimatedCost_AcceptedWithoutError()
    {
        var capturedEvents = new List<UsageEvent>();
        var ledger = new CapturingUsageEventLedger(capturedEvents);

        var evt = BuildEvent("succeeded") with { EstimatedCostUsd = null };
        await ledger.RecordAsync(evt, CancellationToken.None);

        Assert.Single(capturedEvents);
        Assert.Null(capturedEvents[0].EstimatedCostUsd);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private UsageEvent BuildEvent(
        string status,
        int? input = 100,
        int? output = 50) => new(
        OrganizationId: OrgId,
        ClientId: Guid.NewGuid(),
        WorkflowKey: "single-analyzer",
        PipelineStepKey: "ai-analysis",
        ProviderKey: "claude",
        Model: "claude-sonnet-4-5",
        PromptVersion: "v1",
        RequestTimestamp: DateTime.UtcNow.AddSeconds(-1),
        CompletedTimestamp: DateTime.UtcNow,
        DurationMs: 1000,
        InputTokens: input,
        OutputTokens: output,
        EstimatedCostUsd: input.HasValue && output.HasValue ? 0.001m : null,
        RequestSizeBytes: null,
        ResponseSizeBytes: null,
        RetryCount: 0,
        Status: status,
        FailureCode: null,
        CorrelationId: CorrelationId);
}

// -------------------------------------------------------------------
// Test doubles
// -------------------------------------------------------------------

/// <summary>A ledger that captures every event (no failure).</summary>
file sealed class CapturingUsageEventLedger : IUsageEventLedger
{
    private readonly List<UsageEvent> _events;

    public CapturingUsageEventLedger(List<UsageEvent> events) => _events = events;

    public Task RecordAsync(UsageEvent evt, CancellationToken cancellationToken)
    {
        _events.Add(evt);
        return Task.CompletedTask;
    }
}
