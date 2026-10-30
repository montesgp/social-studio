using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Background;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// TDD tests for WU-3 step 3.9: ClientAestheticStrategy (CASE C).
/// Refs: BSP-04; design §4.3; tasks 3.9.
/// </summary>
public sealed class ClientAestheticStrategyTests
{
    // -----------------------------------------------------------------------
    // Policy
    // -----------------------------------------------------------------------

    [Fact]
    public void Policy_IsClientAesthetic()
    {
        var strategy = BuildStrategy();

        Assert.Equal(BackgroundSourcingPolicy.ClientAesthetic, strategy.Policy);
    }

    // -----------------------------------------------------------------------
    // BSP-04: non-empty descriptors → all slides ClientAesthetic(descriptors)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_NonEmptyDescriptors_AllSlidesAreClientAesthetic()
    {
        var descriptors = new[] { "minimalist", "dark palette", "urban" };
        var request = BuildRequest(SlideCount: 3, descriptors: descriptors);
        var strategy = BuildStrategy();

        var plan = strategy.Plan(request);

        Assert.Equal(3, plan.PerSlide.Count);
        foreach (var slide in plan.PerSlide)
        {
            var aesthetic = Assert.IsType<BackgroundSelection.ClientAesthetic>(slide);
            Assert.Equal(descriptors, aesthetic.Descriptors);
        }
    }

    [Fact]
    public void Plan_NonEmptyDescriptors_SingleSlide_IsClientAesthetic()
    {
        var descriptors = new[] { "warm tones" };
        var request = BuildRequest(SlideCount: 1, descriptors: descriptors);
        var strategy = BuildStrategy();

        var plan = strategy.Plan(request);

        Assert.Single(plan.PerSlide);
        var aesthetic = Assert.IsType<BackgroundSelection.ClientAesthetic>(plan.PerSlide[0]);
        Assert.Equal(descriptors, aesthetic.Descriptors);
    }

    // -----------------------------------------------------------------------
    // BSP-04 degradation: empty descriptors → all slides Creative + warning logged
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_EmptyDescriptors_AllSlidesAreCreative()
    {
        var request = BuildRequest(SlideCount: 4, descriptors: Array.Empty<string>());
        var strategy = BuildStrategy();

        var plan = strategy.Plan(request);

        Assert.Equal(4, plan.PerSlide.Count);
        Assert.All(plan.PerSlide, s => Assert.IsType<BackgroundSelection.Creative>(s));
    }

    [Fact]
    public void Plan_EmptyDescriptors_LogsWarning()
    {
        var logger = new CapturingLogger<ClientAestheticStrategy>();
        var request = BuildRequest(SlideCount: 2, descriptors: Array.Empty<string>());
        var strategy = new ClientAestheticStrategy(logger);

        strategy.Plan(request);

        Assert.Contains(logger.LoggedLevels, level => level == LogLevel.Warning);
    }

    // -----------------------------------------------------------------------
    // Generation continues without exception on empty descriptors (BSP-04-b)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_EmptyDescriptors_DoesNotThrow()
    {
        var request = BuildRequest(SlideCount: 3, descriptors: Array.Empty<string>());
        var strategy = BuildStrategy();

        var exception = Record.Exception(() => strategy.Plan(request));

        Assert.Null(exception);
    }

    // -----------------------------------------------------------------------
    // SeedToken preserved
    // -----------------------------------------------------------------------

    [Fact]
    public void Plan_SeedToken_IsPreservedInPlan()
    {
        var request = BuildRequest(SlideCount: 1, descriptors: new[] { "earthy" }, seedToken: "job-42");
        var strategy = BuildStrategy();

        var plan = strategy.Plan(request);

        Assert.Equal("job-42", plan.SeedToken);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ClientAestheticStrategy BuildStrategy()
    {
        return new ClientAestheticStrategy(NullLogger<ClientAestheticStrategy>.Instance);
    }

    private static BackgroundPlanRequest BuildRequest(
        int SlideCount,
        IReadOnlyList<string> descriptors,
        string seedToken = "seed")
    {
        var brand = new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: Array.Empty<string>(),
            VoiceAndTone: null,
            Notes: null);

        return new BackgroundPlanRequest(brand, "carousel", SlideCount, seedToken, descriptors);
    }
}

/// <summary>
/// Simple capturing logger for asserting logging behavior without an external mocking library.
/// </summary>
file sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<LogLevel> _logged = new();

    public IReadOnlyList<LogLevel> LoggedLevels => _logged;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _logged.Add(logLevel);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
