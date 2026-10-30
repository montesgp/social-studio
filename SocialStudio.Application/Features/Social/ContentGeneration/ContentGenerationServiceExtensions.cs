using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Caption;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Carousel;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.ContentIdea;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.CtaVariation;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.FeedPost;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Story;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Hook;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.ReelScript;
using SocialStudio.Application.Features.Social.ContentGeneration.Orchestration;
using SocialStudio.Application.Features.Social.ContentGeneration.Packaging;
using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Composers;
using SocialStudio.Domain.Social.ContentFormats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SocialStudio.Application.Features.Social.ContentGeneration;

/// <summary>
/// Application-layer DI registrations for the content generation feature.
/// Registers all 9 format generators, the orchestrator, visual composition service,
/// per-format visual composers, and the package builder.
/// Infrastructure adapters (IAiCompletionGateway, IClientBrandSnapshotLoader, IContentRenderer,
/// IGeneratedPackageStorage) are registered by the Infrastructure layer.
/// </summary>
public static class ContentGenerationServiceExtensions
{
    /// <summary>
    /// Registers all format content generators, the format generator registry,
    /// the content package orchestrator, visual composers, and the package builder.
    /// </summary>
    public static IServiceCollection AddContentGeneration(this IServiceCollection services)
    {
        // ── Generators ─────────────────────────────────────────────────────────
        // Register each generator under both the concrete type (for typed resolution in tests)
        // and the IFormatContentGenerator interface (for registry enumeration via DI).

        // Shared CarouselGenerator — registered as concrete type so the two surface-scoped
        // decorators (SurfaceScopedCarouselGenerator) can resolve it as their inner dependency.
        // Also registered directly as IFormatContentGenerator so the registry resolves it under
        // the shared "carousel" behavior (the two decorators override FormatKey for story-carousel
        // and feed-carousel respectively).
        services.AddScoped<CarouselGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<CarouselGenerator>());

        // Surface-scoped carousel decorator registrations (ADR-4 / SCENARIO-18).
        // story-carousel and feed-carousel each get their own decorator wrapping the shared generator.
        var storyCarouselDef = FormatCatalog.TryGet("story-carousel")!;
        var feedCarouselDef = FormatCatalog.TryGet("feed-carousel")!;

        services.AddScoped<IFormatContentGenerator>(sp =>
            new SurfaceScopedCarouselGenerator("story-carousel", sp.GetRequiredService<CarouselGenerator>()));
        services.AddScoped<IFormatContentGenerator>(sp =>
            new SurfaceScopedCarouselGenerator("feed-carousel", sp.GetRequiredService<CarouselGenerator>()));

        services.AddScoped<StoryGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<StoryGenerator>());

        services.AddScoped<FeedPostGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<FeedPostGenerator>());

        services.AddScoped<ReelScriptGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<ReelScriptGenerator>());

        services.AddScoped<ContentIdeaGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<ContentIdeaGenerator>());

        services.AddScoped<CtaVariationGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<CtaVariationGenerator>());

        services.AddScoped<CaptionGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<CaptionGenerator>());

        services.AddScoped<HookGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<HookGenerator>());

        // Orchestrator resolves all IFormatContentGenerator registrations via IEnumerable<>
        services.AddScoped<IContentGenerationOrchestrator, ContentPackageOrchestrator>();

        // ── Visual composers ───────────────────────────────────────────────────
        // VisualCompositionService picks them up via IEnumerable<IFormatVisualComposer>.
        // Without these registrations the service resolves with an empty collection and
        // every Compose call throws "No visual composer registered for format key 'X'".

        // Shared CarouselVisualComposer — registered as concrete type for decorator construction.
        services.AddScoped<CarouselVisualComposer>();

        // Surface-scoped carousel visual composer decorators (ADR-4 / SCENARIO-19 / S5).
        services.AddScoped<IFormatVisualComposer>(sp =>
            new SurfaceScopedCarouselVisualComposer(
                storyCarouselDef,
                sp.GetRequiredService<CarouselVisualComposer>()));
        services.AddScoped<IFormatVisualComposer>(sp =>
            new SurfaceScopedCarouselVisualComposer(
                feedCarouselDef,
                sp.GetRequiredService<CarouselVisualComposer>()));

        services.AddScoped<IFormatVisualComposer, StoryVisualComposer>();
        services.AddScoped<IFormatVisualComposer, FeedPostVisualComposer>();
        services.AddScoped<IFormatVisualComposer, HookVisualComposer>();
        services.AddScoped<IFormatVisualComposer, CaptionVisualComposer>();
        services.AddScoped<IFormatVisualComposer, CtaVariationVisualComposer>();
        services.AddScoped<IFormatVisualComposer, ReelScriptVisualComposer>();
        services.AddScoped<IFormatVisualComposer, ContentIdeaVisualComposer>();

        services.AddScoped<IVisualCompositionService, VisualCompositionService>();

        // Package builder (BCL ZipArchive — Application layer, no NuGet)
        services.AddScoped<IGeneratedPackageBuilder, GeneratedPackageBuilder>();

        // Guardrail validator (Application — no Infrastructure types; always registered here).
        services.AddScoped<IVisualGuardrailValidator, VisualGuardrailValidator>();

        // No-op designer: B1a passthrough that keeps designed-raster formats on their CURRENT
        // rendering behavior (SkiaSharp slot raster) until B1b ships the real AI designer.
        // Infrastructure SHOULD override this with AiVisualDesigner in AddRenderingAndPackaging.
        // TryAdd ensures Infrastructure's real registration wins.
        services.TryAddScoped<IVisualDesigner, NoOpVisualDesigner>();

        // End-to-end pipeline used by the background worker: load analysis → orchestrate →
        // compose + render → build + store + persist.
        services.AddScoped<ContentGenerationJobProcessor>();

        return services;
    }
}
