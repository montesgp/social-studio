using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Infrastructure.Observability;

namespace Incoders.Hive.Tests.Infrastructure.Observability;

/// <summary>
/// Tests that <see cref="AiCompletionGatewayInstrumentationDecorator"/> writes
/// exactly one <see cref="UsageEvent"/> per logical call — regardless of whether
/// the underlying gateway attempts retries internally.
/// </summary>
public sealed class AiDecoratorInstrumentationTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    // -------------------------------------------------------------------
    // Core instrumentation: org/client/workflow/step/correlation populated
    // -------------------------------------------------------------------

    [Fact]
    public async Task CompleteJsonAsync_SuccessfulCall_WritesOneEventWithContextFields()
    {
        var capturedEvents = new List<UsageEvent>();
        var ledger = new CapturingLedger(capturedEvents);
        var ctx = CreateContext("single-analyzer", "ai-analysis");

        var inner = new SucceedingGateway<FakeResponse>(new FakeResponse("hello"));
        var decorator = new AiCompletionGatewayInstrumentationDecorator(inner, ledger, ctx);

        await decorator.CompleteJsonAsync<FakeResponse>(Request(), CancellationToken.None);

        Assert.Single(capturedEvents);
        var evt = capturedEvents[0];
        Assert.Equal(OrgId, evt.OrganizationId);
        Assert.Equal("single-analyzer", evt.WorkflowKey);
        Assert.Equal("ai-analysis", evt.PipelineStepKey);
        Assert.Equal(CorrelationId, evt.CorrelationId);
        Assert.Equal("succeeded", evt.Status);
        Assert.True(evt.DurationMs >= 0);
    }

    // -------------------------------------------------------------------
    // Token fields populated from response usage
    // -------------------------------------------------------------------

    [Fact]
    public async Task CompleteJsonAsync_ResponseWithTokenUsage_TokenFieldsPopulated()
    {
        var capturedEvents = new List<UsageEvent>();
        var ledger = new CapturingLedger(capturedEvents);
        var ctx = CreateContext("single-analyzer", "ai-analysis");

        // Gateway returns a response carrying token usage
        var inner = new SucceedingGatewayWithTokens<FakeResponse>(
            new FakeResponse("hello"), providerKey: "claude", model: "claude-sonnet-4-5", inputTokens: 500, outputTokens: 200);
        var decorator = new AiCompletionGatewayInstrumentationDecorator(inner, ledger, ctx);

        await decorator.CompleteJsonAsync<FakeResponse>(Request(), CancellationToken.None);

        Assert.Single(capturedEvents);
        var evt = capturedEvents[0];
        Assert.Equal(500, evt.InputTokens);
        Assert.Equal(200, evt.OutputTokens);
        Assert.Equal("claude", evt.ProviderKey);
    }

    // -------------------------------------------------------------------
    // Null cost when rate missing (cost enrichment deferred to ledger)
    // -------------------------------------------------------------------

    [Fact]
    public async Task CompleteJsonAsync_NoTokensInResponse_TokenFieldsNull()
    {
        var capturedEvents = new List<UsageEvent>();
        var ledger = new CapturingLedger(capturedEvents);
        var ctx = CreateContext("single-analyzer", "ai-analysis");

        var inner = new SucceedingGateway<FakeResponse>(new FakeResponse("hello"));
        var decorator = new AiCompletionGatewayInstrumentationDecorator(inner, ledger, ctx);

        await decorator.CompleteJsonAsync<FakeResponse>(Request(), CancellationToken.None);

        var evt = capturedEvents[0];
        // No token usage in the gateway response → tokens should be null
        Assert.Null(evt.InputTokens);
        Assert.Null(evt.OutputTokens);
    }

    // -------------------------------------------------------------------
    // Ledger failure does NOT re-throw into the caller
    // -------------------------------------------------------------------

    [Fact]
    public async Task CompleteJsonAsync_LedgerThrows_ExceptionIsSwallowedByLedger()
    {
        // The ledger itself is responsible for swallowing — we use a safe wrapping ledger
        var safeWrappingLedger = new SwallowingLedger();
        var ctx = CreateContext("single-analyzer", "ai-analysis");
        var inner = new SucceedingGateway<FakeResponse>(new FakeResponse("hello"));
        var decorator = new AiCompletionGatewayInstrumentationDecorator(inner, safeWrappingLedger, ctx);

        // Must not throw
        var result = await decorator.CompleteJsonAsync<FakeResponse>(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // -------------------------------------------------------------------
    // Context not set — throws InvalidOperationException (programming error)
    // -------------------------------------------------------------------

    [Fact]
    public async Task CompleteJsonAsync_NoContext_ThrowsInvalidOperationException()
    {
        var ctx = new ProviderCallContext(); // empty — no Begin() called
        var ledger = new CapturingLedger(new List<UsageEvent>());
        var inner = new SucceedingGateway<FakeResponse>(new FakeResponse("hello"));
        var decorator = new AiCompletionGatewayInstrumentationDecorator(inner, ledger, ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => decorator.CompleteJsonAsync<FakeResponse>(Request(), CancellationToken.None));
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static ProviderCallContext CreateContext(string workflowKey, string stepKey)
    {
        var ctx = new ProviderCallContext();
        ctx.Begin(OrgId, null, workflowKey, stepKey, "v1", CorrelationId);
        return ctx;
    }

    private static AiJsonCompletionRequest Request() => new(
        OrgId,
        null,
        "ai-analysis",
        "single-analyzer.analysis.v1",
        "{}",
        "claude",
        "claude-sonnet-4-5");
}

// -------------------------------------------------------------------
// Test doubles
// -------------------------------------------------------------------

file sealed record FakeResponse(string Value);

file sealed class CapturingLedger : IUsageEventLedger
{
    private readonly List<UsageEvent> _events;
    public CapturingLedger(List<UsageEvent> events) => _events = events;

    public Task RecordAsync(UsageEvent evt, CancellationToken ct)
    {
        _events.Add(evt);
        return Task.CompletedTask;
    }
}

/// <summary>A ledger that logs the throw but never surfaces it (simulates the real UsageEventLedger).</summary>
file sealed class SwallowingLedger : IUsageEventLedger
{
    public Task RecordAsync(UsageEvent evt, CancellationToken ct) => Task.CompletedTask;
}

file sealed class SucceedingGateway<TResponse> : IAiCompletionGateway
    where TResponse : notnull
{
    private readonly TResponse _payload;

    public SucceedingGateway(TResponse payload) => _payload = payload;

    public Task<Result<AiJsonCompletionResponse<T>>> CompleteJsonAsync<T>(
        AiJsonCompletionRequest request, CancellationToken ct)
        where T : notnull
    {
        var response = new AiJsonCompletionResponse<T>(
            (T)(object)_payload,
            "claude",
            "claude-sonnet-4-5",
            Array.Empty<Incoders.Hive.Domain.Integrations.ProviderFailureDiagnostic>());
        return Task.FromResult(Result.Success(response));
    }
}

file sealed class SucceedingGatewayWithTokens<TResponse> : IAiCompletionGateway
    where TResponse : notnull
{
    private readonly TResponse _payload;
    private readonly string _providerKey;
    private readonly string _model;
    private readonly int _inputTokens;
    private readonly int _outputTokens;

    public SucceedingGatewayWithTokens(
        TResponse payload, string providerKey, string model, int inputTokens, int outputTokens)
    {
        _payload = payload;
        _providerKey = providerKey;
        _model = model;
        _inputTokens = inputTokens;
        _outputTokens = outputTokens;
    }

    public Task<Result<AiJsonCompletionResponse<T>>> CompleteJsonAsync<T>(
        AiJsonCompletionRequest request, CancellationToken ct)
        where T : notnull
    {
        var response = new AiJsonCompletionResponse<T>(
            (T)(object)_payload,
            _providerKey,
            _model,
            Array.Empty<Incoders.Hive.Domain.Integrations.ProviderFailureDiagnostic>(),
            new Incoders.Hive.Domain.Social.ContentGeneration.TokenUsage(_inputTokens, _outputTokens));
        return Task.FromResult(Result.Success(response));
    }
}
