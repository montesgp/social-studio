using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Infrastructure.Observability;
// Disambiguate: old IImageGenerationClient also defines ImageGenerationRequest.
using ImageGenerationRequest = Incoders.Hive.Application.Abstractions.Ai.ImageGenerationRequest;

namespace Incoders.Hive.Tests.Infrastructure.Observability;

/// <summary>
/// WU-5 RED tests: ImageGenerationGatewayInstrumentationDecorator writes exactly one
/// UsageEvent per call (success or failure). Requires active ProviderCallContext.Current.
/// Mirrors AiDecoratorInstrumentationTests.
/// Refs: IMG-02; design §2 instrumentation; tasks 5.3.
/// </summary>
public sealed class ImageGenerationGatewayInstrumentationDecoratorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // 5.3a: Successful call → one UsageEvent with org, provider key, model, success outcome
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_SuccessfulCall_WritesOneEventWithSuccessOutcome()
    {
        var capturedEvents = new List<UsageEvent>();
        var ledger = new CapturingUsageLedger(capturedEvents);
        var ctx = CreateContext("content-generation", "generate-image");

        var bytes = new byte[] { 1, 2, 3 };
        var inner = new SucceedingImageGateway(new ImageGenerationResult(bytes, "image/png", "openai", "dall-e-3"));
        var decorator = new ImageGenerationGatewayInstrumentationDecorator(inner, ledger, ctx);

        var result = await decorator.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(capturedEvents);

        var evt = capturedEvents[0];
        Assert.Equal(OrgId, evt.OrganizationId);
        Assert.Equal("content-generation", evt.WorkflowKey);
        Assert.Equal("generate-image", evt.PipelineStepKey);
        Assert.Equal(CorrelationId, evt.CorrelationId);
        Assert.Equal("succeeded", evt.Status);
        Assert.Equal("openai", evt.ProviderKey);
        Assert.True(evt.DurationMs >= 0);
    }

    // -----------------------------------------------------------------------
    // 5.3b: Failed call → one UsageEvent with failure outcome; event written before propagating
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_FailedCall_WritesOneEventWithFailureOutcomeAndReturnsFailure()
    {
        var capturedEvents = new List<UsageEvent>();
        var ledger = new CapturingUsageLedger(capturedEvents);
        var ctx = CreateContext("content-generation", "generate-image");

        var inner = new FailingImageGateway(Error.Failure("image-gen.failed", "provider error"));
        var decorator = new ImageGenerationGatewayInstrumentationDecorator(inner, ledger, ctx);

        var result = await decorator.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        // Event MUST still be written
        Assert.Single(capturedEvents);

        var evt = capturedEvents[0];
        Assert.Equal("failed", evt.Status);
        Assert.Equal("image-gen.failed", evt.FailureCode);
    }

    // -----------------------------------------------------------------------
    // 5.3c: ProviderCallContext.Current == null → throws (mirrors text decorator behavior)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_NoContext_ThrowsInvalidOperationException()
    {
        var ctx = new ProviderCallContext(); // empty — no Begin() called
        var ledger = new CapturingUsageLedger(new List<UsageEvent>());
        var inner = new SucceedingImageGateway(new ImageGenerationResult([1], "image/png", "openai", "dall-e-3"));
        var decorator = new ImageGenerationGatewayInstrumentationDecorator(inner, ledger, ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => decorator.GenerateAsync(BuildRequest(), CancellationToken.None));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ProviderCallContext CreateContext(string workflowKey, string stepKey)
    {
        var ctx = new ProviderCallContext();
        ctx.Begin(OrgId, null, workflowKey, stepKey, "image-gen/v1", CorrelationId);
        return ctx;
    }

    private static ImageGenerationRequest BuildRequest() => new(
        OrganizationId: OrgId,
        CapabilityKey: "ai-image",
        PromptVersionKey: "image-gen/v1",
        Prompt: "a botanical background",
        Width: 1024,
        Height: 1024,
        PreferredProviderKey: "openai",
        PreferredModel: "dall-e-3");
}

// -----------------------------------------------------------------------
// Test doubles
// -----------------------------------------------------------------------

file sealed class CapturingUsageLedger : IUsageEventLedger
{
    private readonly List<UsageEvent> _events;
    public CapturingUsageLedger(List<UsageEvent> events) => _events = events;
    public Task RecordAsync(UsageEvent evt, CancellationToken ct)
    {
        _events.Add(evt);
        return Task.CompletedTask;
    }
}

file sealed class SucceedingImageGateway : IImageGenerationGateway
{
    private readonly ImageGenerationResult _result;
    public SucceedingImageGateway(ImageGenerationResult result) => _result = result;

    public Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request, CancellationToken ct = default)
        => Task.FromResult(Result.Success(_result));
}

file sealed class FailingImageGateway : IImageGenerationGateway
{
    private readonly Error _error;
    public FailingImageGateway(Error error) => _error = error;

    public Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<ImageGenerationResult>(_error));
}
