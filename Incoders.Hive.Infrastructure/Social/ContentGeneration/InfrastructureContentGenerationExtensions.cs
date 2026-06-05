using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Infrastructure DI registrations for Slice B1b:
/// real Opus designer and Chromium HTML renderer.
///
/// <para>
/// Call <c>services.AddContentGeneration()</c> (Application layer) BEFORE this method.
/// <c>AddB1bRenderingAndDesigner</c> will:
/// <list type="bullet">
/// <item><description>Register <see cref="OpusVisualDesigner"/> as <see cref="IVisualDesigner"/>,
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
    /// Registers B1b Infrastructure services: the configured rendering engine and the real Opus designer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">
    /// Rendering configuration (engine, concurrency, timeout, scale factor). The rendering engine is a
    /// GLOBAL application setting (bound from the <c>Rendering</c> configuration section), NOT per-org.
    /// </param>
    /// <remarks>
    /// <para>
    /// OrganizationId is intentionally absent: it is PER-JOB context and is threaded into
    /// <see cref="IVisualDesigner.ComposeAsync"/> by the pipeline, so <see cref="OpusVisualDesigner"/>
    /// is a normal stateless scoped service that depends only on
    /// <see cref="Application.Abstractions.Ai.IAiCompletionGateway"/>.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Production composition-root entry point: binds <see cref="RenderingOptions"/> from the global
    /// <c>Rendering</c> configuration section and registers the configured engine + the real Opus designer.
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

        return services.AddB1bRenderingAndDesigner(options);
    }

    public static IServiceCollection AddB1bRenderingAndDesigner(
        this IServiceCollection services,
        RenderingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Register the real Opus designer — explicitly overrides the TryAdd no-op from Application.
        // Remove + Add (not TryAdd) guarantees the real designer wins regardless of registration order.
        // No orgId here: it flows per-job through ComposeAsync.
        services.RemoveAll<IVisualDesigner>();
        services.AddScoped<IVisualDesigner, OpusVisualDesigner>();

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

            services.RemoveAll<IContentRenderer>();
            services.AddScoped<IContentRenderer>(sp =>
            {
                var fallback = new Rendering.LocalSvgContentRenderer();
                return new HtmlChromiumContentRenderer(
                    sp.GetRequiredService<ChromiumBrowserPool>(),
                    fallback,
                    options);
            });
        }

        return services;
    }
}
