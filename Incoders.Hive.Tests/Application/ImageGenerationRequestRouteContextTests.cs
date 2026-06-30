using Incoders.Hive.Application.Abstractions.Ai;

namespace Incoders.Hive.Tests.Application;

public sealed class ImageGenerationRequestRouteContextTests
{
    [Fact]
    public void Constructor_DefaultsRouteContextToSingleAnalyzerGenerateImage()
    {
        var request = new ImageGenerationRequest(
            Guid.NewGuid(),
            "ai-image",
            "image-gen/v1",
            "prompt",
            1024,
            1024,
            PreferredProviderKey: null,
            PreferredModel: null);

        Assert.Equal("single-analyzer", request.WorkflowKey);
        Assert.Equal("content-analysis", request.AnalysisType);
        Assert.Equal("generate-image", request.StepKey);
        Assert.Null(request.OperationKey);
        Assert.Null(request.NetworkKey);
    }

    [Fact]
    public void Constructor_AllowsCustomProviderAgnosticRouteContext()
    {
        var request = new ImageGenerationRequest(
            Guid.NewGuid(),
            "ai-image",
            "image-gen/v1",
            "prompt",
            1024,
            1024,
            PreferredProviderKey: null,
            PreferredModel: null,
            WorkflowKey: "content-generator",
            AnalysisType: "asset-generation",
            StepKey: "generate-cover",
            OperationKey: "carousel",
            NetworkKey: "instagram");

        Assert.Equal("content-generator", request.WorkflowKey);
        Assert.Equal("asset-generation", request.AnalysisType);
        Assert.Equal("generate-cover", request.StepKey);
        Assert.Equal("carousel", request.OperationKey);
        Assert.Equal("instagram", request.NetworkKey);
    }
}
