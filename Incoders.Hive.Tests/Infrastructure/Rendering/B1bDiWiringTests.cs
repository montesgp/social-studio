using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Rendering;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Tests.Infrastructure.Rendering;

/// <summary>
/// B1b TDD tests for DI wiring and engine selection (Task 9.1).
/// Verifies:
///   - AiVisualDesigner is registered as IVisualDesigner (replaces no-op)
///   - Engine selection: Skia config → LocalSvgContentRenderer; Chromium config → HtmlChromiumContentRenderer
///   - IsActive = true for AiVisualDesigner
/// Design: Decision 11 (config-selectable engine), 13 (pipeline wiring).
/// </summary>
public sealed class B1bDiWiringTests
{
    // -----------------------------------------------------------------------
    // Task 9.1 RED: AiVisualDesigner registered as IVisualDesigner
    // -----------------------------------------------------------------------

    [Fact]
    public void DI_AiVisualDesigner_RegisteredAs_IVisualDesigner()
    {
        var sp = BuildServiceProvider(engine: "chromium");
        var designer = sp.GetRequiredService<IVisualDesigner>();
        Assert.IsType<AiVisualDesigner>(designer);
    }

    [Fact]
    public void DI_AiVisualDesigner_IsActive_IsTrue()
    {
        var sp = BuildServiceProvider(engine: "chromium");
        var designer = sp.GetRequiredService<IVisualDesigner>();
        Assert.True(designer.IsActive,
            "AiVisualDesigner.IsActive must be true — it is the real, active designer.");
    }

    // -----------------------------------------------------------------------
    // Task 9.1 RED: Config-selectable renderer engine
    // -----------------------------------------------------------------------

    [Fact]
    public void DI_SkiaEngine_ResolvesLocalSvgContentRenderer()
    {
        var sp = BuildServiceProvider(engine: "skia");
        var renderer = sp.GetRequiredService<IContentRenderer>();
        Assert.IsType<LocalSvgContentRenderer>(renderer);
    }

    [Fact]
    public void DI_ChromiumEngine_ResolvesHtmlChromiumContentRenderer()
    {
        var sp = BuildServiceProvider(engine: "chromium");
        var renderer = sp.GetRequiredService<IContentRenderer>();
        Assert.IsType<HtmlChromiumContentRenderer>(renderer);
    }

    [Fact]
    public void DI_DefaultEngine_ResolvesHtmlChromiumContentRenderer()
    {
        // Default engine is Chromium per RenderingOptions defaults
        var sp = BuildServiceProvider(engine: null);
        var renderer = sp.GetRequiredService<IContentRenderer>();
        Assert.IsType<HtmlChromiumContentRenderer>(renderer);
    }

    /// <summary>
    /// B3 WU-1 D3: when Chromium engine is configured, IContentRendererWithGeometry MUST
    /// resolve to HtmlChromiumContentRenderer — the same instance as IContentRenderer.
    /// </summary>
    [Fact]
    public void DI_ChromiumEngine_ResolvesIContentRendererWithGeometry()
    {
        var sp = BuildServiceProvider(engine: "chromium");
        var geometryRenderer = sp.GetRequiredService<IContentRendererWithGeometry>();
        Assert.IsType<HtmlChromiumContentRenderer>(geometryRenderer);
    }

    /// <summary>
    /// B3 WU-1 D3: IContentRenderer and IContentRendererWithGeometry must resolve to the
    /// SAME HtmlChromiumContentRenderer instance within the same DI scope (no double allocation).
    /// </summary>
    [Fact]
    public void DI_ChromiumEngine_IContentRendererAndGeometryRenderer_SameInstance()
    {
        using var sp = BuildServiceProvider(engine: "chromium");
        using var scope = sp.CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<IContentRenderer>();
        var geometryRenderer = scope.ServiceProvider.GetRequiredService<IContentRendererWithGeometry>();
        Assert.Same(renderer, geometryRenderer);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ServiceProvider BuildServiceProvider(string? engine)
    {
        var services = new ServiceCollection();

        // Stub AI gateway — AiVisualDesigner takes IAiCompletionGateway
        services.AddScoped<IAiCompletionGateway, StubAiGatewayForDi>();

        // Stub AI selector — AiVisualDesigner resolves the org's provider/model through it.
        services.AddScoped<IDefaultAiSelector, StubAiSelectorForDi>();

        // Logging — required by HtmlChromiumContentRenderer (ILogger<HtmlChromiumContentRenderer>)
        services.AddLogging();

        // Content generation Application-layer registrations (no-op designer via TryAdd)
        services.AddContentGeneration();

        // B1b Infrastructure DI (rendering + real designer override)
        var options = new RenderingOptions
        {
            Engine = engine?.ToLowerInvariant() switch
            {
                "skia" => RenderingEngine.Skia,
                "chromium" => RenderingEngine.Chromium,
                _ => RenderingEngine.Chromium // default
            }
        };
        services.AddB1bRenderingAndDesigner(options);

        return services.BuildServiceProvider();
    }
}

// -----------------------------------------------------------------------
// Stubs
// -----------------------------------------------------------------------

file sealed class StubAiGatewayForDi : IAiCompletionGateway
{
    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request, CancellationToken ct = default) where TResponse : notnull =>
        throw new NotImplementedException("DI stub — not called in wiring test.");
}

file sealed class StubAiSelectorForDi : IDefaultAiSelector
{
    public Task<Result<DefaultAiSelection>> SelectAsync(Guid organizationId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(new DefaultAiSelection("claude", "claude-sonnet-4-6")));
}
