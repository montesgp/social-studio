using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Caption;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ContentIdea;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.CtaVariation;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.FeedPost;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Story;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Hook;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ReelScript;
using Incoders.Hive.Application.Features.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration;

/// <summary>
/// Verifies that all format generators and the orchestrator are resolvable from DI.
/// Updated for the FormatCatalog refactor: story-carousel and feed-carousel are registered
/// as SurfaceScopedCarouselGenerator decorators in addition to the shared CarouselGenerator.
/// </summary>
public sealed class ContentGenerationDiResolutionTests
{
    [Fact]
    public void DI_ResolvesAllFormatGenerators()
    {
        var sp = BuildServiceProvider();

        var generators = sp.GetServices<IFormatContentGenerator>().ToList();

        // CarouselGenerator (shared) + 2 surface-scoped decorators (story-carousel, feed-carousel)
        // + StoryGenerator + FeedPostGenerator + ReelScriptGenerator
        // + ContentIdeaGenerator + CtaVariationGenerator + CaptionGenerator + HookGenerator = 10
        Assert.Equal(10, generators.Count);

        var keys = generators.Select(g => g.FormatKey).ToHashSet();
        Assert.Contains("carousel", keys);           // shared CarouselGenerator (legacy key)
        Assert.Contains("story-carousel", keys);     // SurfaceScopedCarouselGenerator decorator
        Assert.Contains("feed-carousel", keys);      // SurfaceScopedCarouselGenerator decorator
        Assert.Contains("story", keys);
        Assert.Contains("feed-post", keys);
        Assert.Contains("reel-script", keys);
        Assert.Contains("content-idea", keys);
        Assert.Contains("cta-variation", keys);
        Assert.Contains("caption", keys);
        Assert.Contains("hook", keys);
    }

    [Fact]
    public void DI_ResolvesContentPackageOrchestrator()
    {
        var sp = BuildServiceProvider();

        var orchestrator = sp.GetService<IContentGenerationOrchestrator>();

        Assert.NotNull(orchestrator);
        Assert.IsType<ContentPackageOrchestrator>(orchestrator);
    }

    [Fact]
    public void DI_EachFormatGeneratorTypedRegistration_IsResolvable_IncludingStoryAndFeedPost()
    {
        var sp = BuildServiceProvider();

        Assert.NotNull(sp.GetService<CarouselGenerator>());
        Assert.NotNull(sp.GetService<StoryGenerator>());
        Assert.NotNull(sp.GetService<FeedPostGenerator>());
        Assert.NotNull(sp.GetService<ReelScriptGenerator>());
        Assert.NotNull(sp.GetService<ContentIdeaGenerator>());
        Assert.NotNull(sp.GetService<CtaVariationGenerator>());
        Assert.NotNull(sp.GetService<CaptionGenerator>());
        Assert.NotNull(sp.GetService<HookGenerator>());
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Minimal stubs for required dependencies
        services.AddScoped<IAiCompletionGateway, StubAiCompletionGateway>();
        services.AddScoped<IClientBrandSnapshotLoader, StubBrandSnapshotLoader>();
        services.AddScoped<IWorkflowConfigurationResolver, StubWorkflowResolver>();
        services.AddScoped<ProviderCallContext>();

        // The method under test
        services.AddContentGeneration();

        return services.BuildServiceProvider();
    }
}

// -------------------------------------------------------------------
// Stubs
// -------------------------------------------------------------------

file sealed class StubAiCompletionGateway : IAiCompletionGateway
{
    public Task<Incoders.Hive.Domain.Common.Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request, CancellationToken ct) where TResponse : notnull =>
        throw new NotImplementedException("Stub — not called in DI resolution test");
}

file sealed class StubBrandSnapshotLoader : IClientBrandSnapshotLoader
{
    public Task<Incoders.Hive.Domain.Social.ContentGeneration.BrandSnapshot> LoadAsync(
        Guid organizationId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(Incoders.Hive.Domain.Social.ContentGeneration.BrandSnapshot.Empty);
}

file sealed class StubWorkflowResolver : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
        [
            new WorkflowStepRouteCandidate(Guid.NewGuid(), "claude", Guid.NewGuid(),
                IntegrationConnectionMode.BringYourOwnKey, 1, "claude-sonnet-4-6", null, true)
        ]));
}
