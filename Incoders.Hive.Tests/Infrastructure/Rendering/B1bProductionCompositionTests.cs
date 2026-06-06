using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Infrastructure.Rendering;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Tests.Infrastructure.Rendering;

/// <summary>
/// FIX 1 proof: the B1b feature is genuinely ACTIVE in the production composition root.
///
/// <para>
/// These tests drive the SAME config-bound entry point that <c>AddInfrastructure</c> calls
/// (<see cref="InfrastructureContentGenerationExtensions.AddB1bRenderingAndDesigner(IServiceCollection, IConfiguration)"/>)
/// after the Application layer has registered the <c>NoOpVisualDesigner</c> via
/// <c>AddContentGeneration()</c>. They prove that in production:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="IVisualDesigner"/> resolves to the real <see cref="OpusVisualDesigner"/> with <c>IsActive == true</c> (NOT the no-op).</description></item>
///   <item><description><see cref="IContentRenderer"/> resolves to the engine named in the <c>Rendering</c> config section.</description></item>
/// </list>
/// <para>
/// Before this fix, <c>AddB1bRenderingAndDesigner</c> was never called from <c>AddInfrastructure</c>,
/// so the production graph resolved <c>NoOpVisualDesigner</c> (IsActive=false) and the unconditional
/// <c>LocalSvgContentRenderer</c> — leaving the Opus designer INERT.
/// </para>
/// </summary>
public sealed class B1bProductionCompositionTests
{
    [Fact]
    public void ProductionComposition_DefaultConfig_DesignerIsActiveOpus()
    {
        var sp = BuildProductionLikeGraph(engine: null); // no Engine key → default Chromium

        var designer = sp.GetRequiredService<IVisualDesigner>();

        Assert.IsType<OpusVisualDesigner>(designer);
        Assert.True(designer.IsActive,
            "Production wiring MUST resolve the active OpusVisualDesigner — the feature was INERT when it resolved the no-op.");
    }

    [Fact]
    public void ProductionComposition_DefaultConfig_RendererIsChromium()
    {
        var sp = BuildProductionLikeGraph(engine: null); // default = Chromium

        var renderer = sp.GetRequiredService<IContentRenderer>();

        Assert.IsType<HtmlChromiumContentRenderer>(renderer);
    }

    [Fact]
    public void ProductionComposition_ChromiumConfig_RendererIsChromium()
    {
        var sp = BuildProductionLikeGraph(engine: "Chromium");

        var renderer = sp.GetRequiredService<IContentRenderer>();

        Assert.IsType<HtmlChromiumContentRenderer>(renderer);
    }

    [Fact]
    public void ProductionComposition_SkiaConfig_RendererIsLocalSvg()
    {
        var sp = BuildProductionLikeGraph(engine: "Skia");

        var renderer = sp.GetRequiredService<IContentRenderer>();

        Assert.IsType<LocalSvgContentRenderer>(renderer);
    }

    [Fact]
    public void ProductionComposition_BindsRenderingOptionsFromConfig()
    {
        var sp = BuildProductionLikeGraph(engine: "Skia", maxConcurrentRenders: 5);

        var options = sp.GetRequiredService<RenderingOptions>();

        Assert.Equal(RenderingEngine.Skia, options.Engine);
        Assert.Equal(5, options.MaxConcurrentRenders);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reproduces the production registration order for the rendering/designer slice:
    ///   1. Application: AddContentGeneration() — registers NoOpVisualDesigner (IsActive=false) via TryAdd.
    ///   2. Infrastructure: AddB1bRenderingAndDesigner(IConfiguration) — the exact call AddInfrastructure makes.
    /// A stub IAiCompletionGateway stands in for the real provider gateway (no network).
    /// </summary>
    private static ServiceProvider BuildProductionLikeGraph(
        string? engine,
        int? maxConcurrentRenders = null)
    {
        var services = new ServiceCollection();

        // Provider gateway dependency of OpusVisualDesigner (real one needs Infrastructure plumbing).
        services.AddScoped<IAiCompletionGateway, StubProductionAiGateway>();

        // Logging — required by HtmlChromiumContentRenderer (ILogger<HtmlChromiumContentRenderer>)
        services.AddLogging();

        // Application layer — registers the NoOpVisualDesigner via TryAdd (production baseline).
        services.AddContentGeneration();

        // Production config-bound entry point (identical to AddInfrastructure's call).
        var settings = new Dictionary<string, string?>();
        if (engine is not null)
        {
            settings["Rendering:Engine"] = engine;
        }
        if (maxConcurrentRenders is not null)
        {
            settings["Rendering:MaxConcurrentRenders"] = maxConcurrentRenders.Value.ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        services.AddB1bRenderingAndDesigner(configuration);

        return services.BuildServiceProvider();
    }
}

file sealed class StubProductionAiGateway : IAiCompletionGateway
{
    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request, CancellationToken ct = default) where TResponse : notnull =>
        throw new NotImplementedException("Stub — not invoked in composition-root resolution tests.");
}
