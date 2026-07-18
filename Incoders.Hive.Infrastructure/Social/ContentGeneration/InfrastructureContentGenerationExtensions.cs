using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Infrastructure.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Infrastructure DI registrations for Slice B1b:
/// real AI designer and Chromium HTML renderer.
///
/// <para>
/// Call <c>services.AddContentGeneration()</c> (Application layer) BEFORE this method.
/// <c>AddB1bRenderingAndDesigner</c> will:
/// <list type="bullet">
/// <item><description>Register <see cref="AiVisualDesigner"/> as <see cref="IVisualDesigner"/>,
/// replacing the <c>NoOpVisualDesigner</c> that was registered by
/// <c>AddContentGeneration()</c> via <c>TryAddScoped</c>.</description></item>
/// <item><description>Register <see cref="HtmlChromiumContentRenderer"/> (or
/// <see cref="LocalSvgContentRenderer"/> when engine is Skia) as <see cref="IContentRenderer"/>.</description></item>
/// <item><description>Register <see cref="ChromiumBrowserPool"/> as a singleton when engine is Chromium.</description></item>
/// </list>
/// </para>
///
/// <para>
/// Design: Decision 11 (engine config), Decision 13 (pipeline wiring override).
/// </para>
/// </summary>
public static class InfrastructureContentGenerationExtensions
{
    /// <summary>
    /// Registers B1b Infrastructure services: the configured rendering engine and the real AI designer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">
    /// Rendering configuration (engine, concurrency, timeout, scale factor). The rendering engine is a
    /// GLOBAL application setting (bound from the <c>Rendering</c> configuration section), NOT per-org.
    /// </param>
    /// <remarks>
    /// <para>
    /// OrganizationId is intentionally absent: it is PER-JOB context and is threaded into
    /// <see cref="IVisualDesigner.ComposeAsync"/> by the pipeline, so <see cref="AiVisualDesigner"/>
    /// is a normal stateless scoped service that depends only on
    /// <see cref="Application.Abstractions.Ai.IAiCompletionGateway"/>.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Production composition-root entry point: binds <see cref="RenderingOptions"/> from the global
    /// <c>Rendering</c> configuration section and registers the configured engine + the real AI designer.
    /// This is the EXACT path used by <c>AddInfrastructure</c>, so a test that calls this method proves
    /// the feature is genuinely ACTIVE in production (not just via the lower-level overload).
    /// </summary>
    public static IServiceCollection AddB1bRenderingAndDesigner(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RenderingOptions();
        configuration.GetSection(RenderingOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        // issue #67 (BAPA-CG-01/02): bind the first-time aesthetic-gating options so both
        // ClientAestheticEnsureFreshService (poll interval) and ContentGenerationJobProcessor
        // (max wait, feature toggle) resolve them via IOptions<T> — see design.md Decision 2.
        services.AddOptions<Application.Features.Social.ContentGeneration.Shared.ContentGenerationAestheticGatingOptions>()
            .Bind(configuration.GetSection(
                Application.Features.Social.ContentGeneration.Shared.ContentGenerationAestheticGatingOptions.SectionName));

        // Per-job wall-clock budget for ContentGenerationBackgroundWorker (issue: job 8757e85a /
        // 07771831 — see ContentGenerationWorkerOptions doc). Bound here so the worker resolves it
        // via IOptions<T>, consistent with the AestheticGatingOptions binding above.
        services.AddOptions<Application.Features.Social.ContentGeneration.Shared.ContentGenerationWorkerOptions>()
            .Bind(configuration.GetSection(
                Application.Features.Social.ContentGeneration.Shared.ContentGenerationWorkerOptions.SectionName));

        return services.AddB1bRenderingAndDesigner(options);
    }

    public static IServiceCollection AddB1bRenderingAndDesigner(
        this IServiceCollection services,
        RenderingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Register the real AI designer — explicitly overrides the TryAdd no-op from Application.
        // Remove + Add (not TryAdd) guarantees the real designer wins regardless of registration order.
        // No orgId here: it flows per-job through ComposeAsync.
        services.RemoveAll<IVisualDesigner>();
        services.AddScoped<IVisualDesigner, AiVisualDesigner>();

        if (options.Engine == RenderingEngine.Skia)
        {
            // Skia engine: register LocalSvgContentRenderer directly, replacing any prior registration.
            services.RemoveAll<IContentRenderer>();
            services.AddScoped<IContentRenderer, Rendering.LocalSvgContentRenderer>();
        }
        else
        {
            // Chromium engine: register pool as singleton, wrap in HtmlChromiumContentRenderer.
            // Replace the default LocalSvgContentRenderer registered by AddRenderingAndPackaging.
            var pool = new ChromiumBrowserPool(options);
            services.AddSingleton(pool);

            // Register HtmlChromiumContentRenderer as a named scoped instance, then expose it
            // via both IContentRenderer and IContentRendererWithGeometry (D3: geometry capture port).
            services.RemoveAll<IContentRenderer>();
            services.RemoveAll<IContentRendererWithGeometry>();
            services.AddScoped<HtmlChromiumContentRenderer>(sp =>
            {
                var fallback = new Rendering.LocalSvgContentRenderer();
                return new HtmlChromiumContentRenderer(
                    sp.GetRequiredService<ChromiumBrowserPool>(),
                    fallback,
                    options,
                    sp.GetRequiredService<ILogger<HtmlChromiumContentRenderer>>());
            });
            services.AddScoped<IContentRenderer>(sp =>
                sp.GetRequiredService<HtmlChromiumContentRenderer>());
            services.AddScoped<IContentRendererWithGeometry>(sp =>
                sp.GetRequiredService<HtmlChromiumContentRenderer>());
        }

        // Register the PPTX export adapter (B3 WU-2 — provider-neutral port, OpenXML adapter).
        // studio-background-asset-ref (Slice 1, BGREF-03): the adapter now takes IGeneratedAssetStorage
        // as a ctor dependency to resolve a storage-path BackgroundImageRef/FlattenedImageRef.
        // IGeneratedAssetStorage is registered by AddInfrastructure (SupabaseStorageAdapter via
        // AddHttpClient) — no additional registration is needed here; DI resolves the ctor param.
        services.AddScoped<IPptxExportAdapter, PptxExportAdapter>();

        // ── Background sourcing strategy family (WU-5 §5.5/5.6) ──────────────
        // All four strategies are registered as IBackgroundSourcingStrategy so
        // BackgroundStrategyResolver can build its policy→strategy map via IEnumerable<>.
        // LegacyTierStrategy is also registered directly as the named legacy fallback
        // that the resolver passes to its _legacyFallback parameter.
        services.AddScoped<LegacyTierStrategy>();
        services.AddScoped<IBackgroundSourcingStrategy>(sp => sp.GetRequiredService<LegacyTierStrategy>());
        services.AddScoped<IBackgroundSourcingStrategy, ClientDefinedSetStrategy>();
        services.AddScoped<IBackgroundSourcingStrategy, AiPaletteStrategy>();
        services.AddScoped<IBackgroundSourcingStrategy, ClientAestheticStrategy>();

        // Resolver: receives IEnumerable<IBackgroundSourcingStrategy> (all four above) +
        // the legacy fallback singleton so null/Unset/unknown routes to legacy behavior.
        services.AddScoped<IBackgroundStrategyResolver>(sp =>
            new BackgroundStrategyResolver(
                sp.GetRequiredService<IEnumerable<IBackgroundSourcingStrategy>>(),
                sp.GetRequiredService<LegacyTierStrategy>()));

        // CASE C aesthetic profile reader (CAG-CG-05 / WU-5 §5.11):
        // Reads DetectedBackgroundImages, VoiceTone (from ai_analysis_json) and
        // AverageSlidesPerCarousel (from server_derived_stats_json) of the most recent
        // Analyzed ProfileAiAnalysis for a client.
        // Replaces the narrow IClientAestheticDescriptorReader (descriptor-only) with the
        // richer IClientAestheticProfileReader.
        // IClientAestheticDescriptorReader registration is retained for backward compatibility
        // with existing tests/callers until fully removed.
        services.AddScoped<IClientAestheticDescriptorReader, EfClientAestheticDescriptorReader>();
        services.AddScoped<IClientAestheticProfileReader, EfClientAestheticProfileReader>();

        // EnsureFresh service (CAG-CG-01/02/03/04 / WU-3): fire-and-forget background refresh
        // orchestrator; registers against the port so ContentGenerationJobProcessor can inject it.
        services.AddScoped<IClientAestheticEnsureFreshService, ClientAestheticEnsureFreshService>();

        // Phase 2 (CAG2-PA-02/03): durable post-media archiver + multimodal aesthetic extractor.
        // Injected (optionally) into ProfileAnalyzerOrchestrator to enrich ClientAesthetic clients.
        services.AddScoped<IClientPostMediaArchiver, SupabasePostMediaArchiver>();
        services.AddScoped<IClientAestheticStyleExtractor, MultimodalAestheticStyleExtractor>();

        // CAC-IMG-03: production image guardrail. Registered so ClientAestheticImageBackgroundService
        // resolves a real validator (its ctor param is optional/null by default) and the retry/degrade
        // path activates — rejects near-black/flat generated backgrounds instead of shipping them.
        services.AddScoped<IImageGuardrailValidator, DegenerateImageGuardrailValidator>();

        return services;
    }
}
