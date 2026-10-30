using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Unit tests for FormatGenerationResult and its status semantics.
/// RED phase: these tests reference types that do not exist yet.
/// </summary>
public sealed class FormatGenerationResultTests
{
    private sealed record TestPayload(string Value);

    [Fact]
    public void Succeeded_CreatesResultWithSucceededStatusAndPayload()
    {
        var payload = new TestPayload("content");

        var result = FormatGenerationResult<TestPayload>.Succeeded(
            payload: payload,
            promptVersion: "carousel.v1",
            inferredFields: []);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.Same(payload, result.Payload);
        Assert.Equal("carousel.v1", result.PromptVersion);
        Assert.Empty(result.InferredFields);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Succeeded_WithInferredFields_ExposesFields()
    {
        var payload = new TestPayload("content");
        var inferred = new[] { "brand.voice", "brand.palette.primary" };

        var result = FormatGenerationResult<TestPayload>.Succeeded(
            payload: payload,
            promptVersion: "carousel.v1",
            inferredFields: inferred);

        Assert.Equal(FormatGenerationStatus.Succeeded, result.Status);
        Assert.Equal(2, result.InferredFields.Count);
        Assert.Contains("brand.voice", result.InferredFields);
        Assert.Contains("brand.palette.primary", result.InferredFields);
    }

    [Fact]
    public void Failed_CreatesResultWithFailedStatusAndReason()
    {
        var result = FormatGenerationResult<TestPayload>.Failed(
            promptVersion: "carousel.v1",
            failureReason: "schema-validation");

        Assert.Equal(FormatGenerationStatus.Failed, result.Status);
        Assert.Null(result.Payload);
        Assert.Equal("carousel.v1", result.PromptVersion);
        Assert.Equal("schema-validation", result.FailureReason);
        Assert.Empty(result.InferredFields);
    }

    [Fact]
    public void AsObject_SucceededResult_ReturnsObjectVersionWithPayload()
    {
        var payload = new TestPayload("content");
        var result = FormatGenerationResult<TestPayload>.Succeeded(
            payload: payload,
            promptVersion: "carousel.v1",
            inferredFields: ["brand.voice"]);

        var boxed = result.AsObject();

        Assert.Equal(FormatGenerationStatus.Succeeded, boxed.Status);
        Assert.Equal(payload, boxed.Payload);
        Assert.Equal("carousel.v1", boxed.PromptVersion);
        Assert.Single(boxed.InferredFields);
        Assert.Null(boxed.FailureReason);
    }

    [Fact]
    public void AsObject_FailedResult_ReturnsObjectVersionWithNullPayload()
    {
        var result = FormatGenerationResult<TestPayload>.Failed(
            promptVersion: "carousel.v1",
            failureReason: "provider-unavailable");

        var boxed = result.AsObject();

        Assert.Equal(FormatGenerationStatus.Failed, boxed.Status);
        Assert.Null(boxed.Payload);
        Assert.Equal("provider-unavailable", boxed.FailureReason);
    }
}
