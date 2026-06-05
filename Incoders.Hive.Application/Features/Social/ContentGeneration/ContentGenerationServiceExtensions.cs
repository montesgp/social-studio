using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Caption;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ContentIdea;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.CtaVariation;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Flyer;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Hook;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ReelScript;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Packaging;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration;

/// <summary>
/// Application-layer DI registrations for the content generation feature.
/// Registers all 7 format generators, the orchestrator, visual composition service,
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
        // Register each generator under both the concrete type (for typed resolution in tests)
        // and the IFormatContentGenerator interface (for registry enumeration via DI).
        services.AddScoped<CarouselGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<CarouselGenerator>());

        services.AddScoped<FlyerGenerator>();
        services.AddScoped<IFormatContentGenerator>(sp => sp.GetRequiredService<FlyerGenerator>());

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

        // Per-format visual composers — VisualCompositionService picks them up via
        // IEnumerable<IFormatVisualComposer>. Without these explicit registrations the service
        // resolves with an empty collection and every Compose call throws
        // "No visual composer registered for format key 'X'" at runtime.
        services.AddScoped<IFormatVisualComposer, CarouselVisualComposer>();
        services.AddScoped<IFormatVisualComposer, FlyerVisualComposer>();
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
        // rendering behavior (SkiaSharp slot raster) until B1b ships the real Opus designer.
        // Infrastructure SHOULD override this with OpusVisualDesigner in AddRenderingAndPackaging.
        // TryAdd ensures Infrastructure's real registration wins.
        services.TryAddScoped<IVisualDesigner, NoOpVisualDesigner>();

        // End-to-end pipeline used by the background worker: load analysis → orchestrate →
        // compose + render → build + store + persist.
        services.AddScoped<ContentGenerationJobProcessor>();

        return services;
    }
}
