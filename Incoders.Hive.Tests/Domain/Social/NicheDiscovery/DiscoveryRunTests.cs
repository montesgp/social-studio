using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Domain.Social.NicheDiscovery;

public sealed class DiscoveryRunTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrganizationId = Guid.NewGuid();

    private static DiscoveryRun CreateValidRun(DateTime? nowUtc = null)
    {
        var window = DiscoveryWindow.Create(30).Value;
        var result = DiscoveryRun.Create(
            OrganizationId,
            sourceMarketCode: "es-AR",
            targetMarketCode: "es-AR",
            networkKey: "youtube",
            contentFormat: "long",
            window,
            DiscoveryDepth.Quick,
            configurationSnapshotJson: "{\"originMapping\":{},\"weights\":{},\"depthCeiling\":400}",
            nicheId: null,
            nowUtc ?? Now);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void Create_WithAllRequiredParameters_PersistsThemAndSnapshotsConfiguration()
    {
        var window = DiscoveryWindow.Create(30).Value;

        var result = DiscoveryRun.Create(
            OrganizationId,
            sourceMarketCode: "es-AR",
            targetMarketCode: "es-AR",
            networkKey: "youtube",
            contentFormat: "long",
            window,
            DiscoveryDepth.Quick,
            configurationSnapshotJson: "{\"originMapping\":{},\"weights\":{},\"depthCeiling\":400}",
            nicheId: null,
            Now);

        Assert.True(result.IsSuccess);
        var run = result.Value;

        Assert.Equal(OrganizationId, run.OrganizationId);
        Assert.Equal("es-AR", run.SourceMarketCode.Value);
        Assert.Equal("es-AR", run.TargetMarketCode.Value);
        Assert.Equal("youtube", run.NetworkKey);
        Assert.Equal("long", run.ContentFormat);
        Assert.Equal(30, run.Window.Days);
        Assert.Equal(DiscoveryDepth.Quick, run.Depth);
        Assert.Equal("{\"originMapping\":{},\"weights\":{},\"depthCeiling\":400}", run.ConfigurationSnapshotJson);
        Assert.Equal(DiscoveryRunStatus.Requested, run.Status);
        Assert.Equal(Now, run.CreatedAtUtc);
    }

    [Theory]
    [InlineData("not-a-market-code")]
    [InlineData("esAR")]
    [InlineData("es")]
    [InlineData("spa-ARG")]
    [InlineData("e5-AR")]
    public void Create_WithMalformedMarketCode_FailsInsteadOfStoringIt(string malformed)
    {
        var window = DiscoveryWindow.Create(30).Value;

        var asSource = DiscoveryRun.Create(
            OrganizationId, malformed, "es-AR", "youtube", "long", window, DiscoveryDepth.Quick, "{}", null, Now);
        var asTarget = DiscoveryRun.Create(
            OrganizationId, "es-AR", malformed, "youtube", "long", window, DiscoveryDepth.Quick, "{}", null, Now);

        Assert.True(asSource.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.MarketCodeInvalid, asSource.Error);
        Assert.True(asTarget.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.MarketCodeInvalid, asTarget.Error);
    }

    [Fact]
    public void Create_WithNonCanonicalMarketCasing_StoresTheCanonicalForm()
    {
        var window = DiscoveryWindow.Create(30).Value;

        var result = DiscoveryRun.Create(
            OrganizationId, "ES-ar", "PT-br", "youtube", "long", window, DiscoveryDepth.Quick, "{}", null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("es-AR", result.Value.SourceMarketCode.Value);
        Assert.Equal("pt-BR", result.Value.TargetMarketCode.Value);
    }

    [Fact]
    public void TwoRuns_WithIdenticalParameters_ProduceComparableSnapshots()
    {
        var window = DiscoveryWindow.Create(30).Value;
        const string snapshot = "{\"originMapping\":{},\"weights\":{},\"depthCeiling\":400}";

        var first = DiscoveryRun.Create(
            OrganizationId, "es-AR", "es-AR", "youtube", "long", window, DiscoveryDepth.Quick, snapshot, null, Now);
        var second = DiscoveryRun.Create(
            OrganizationId, "es-AR", "es-AR", "youtube", "long", window, DiscoveryDepth.Quick, snapshot, null, Now);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.ConfigurationSnapshotJson, second.Value.ConfigurationSnapshotJson);
        Assert.Equal(first.Value.Window, second.Value.Window);
        Assert.Equal(first.Value.Depth, second.Value.Depth);
    }

    [Fact]
    public void Start_FromRequested_TransitionsToRunning()
    {
        var run = CreateValidRun();

        run.Start(Now.AddMinutes(1));

        Assert.Equal(DiscoveryRunStatus.Running, run.Status);
        Assert.Equal(Now.AddMinutes(1), run.StartedAtUtc);
    }

    [Fact]
    public void Complete_FromRunning_TransitionsToCompleted()
    {
        var run = CreateValidRun();
        run.Start(Now.AddMinutes(1));

        run.Complete(Now.AddMinutes(5));

        Assert.Equal(DiscoveryRunStatus.Completed, run.Status);
        Assert.Equal(Now.AddMinutes(5), run.CompletedAtUtc);
    }

    [Fact]
    public void CompletePartially_FromRunning_TransitionsToPartiallyCompleted()
    {
        var run = CreateValidRun();
        run.Start(Now.AddMinutes(1));

        run.CompletePartially(Now.AddMinutes(5));

        Assert.Equal(DiscoveryRunStatus.PartiallyCompleted, run.Status);
    }

    [Fact]
    public void Start_FromNonRequested_ThrowsInvalidOperationException()
    {
        var run = CreateValidRun();
        run.Start(Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => run.Start(Now.AddMinutes(2)));
    }

    [Fact]
    public void Fail_UnconfiguredOrigin_TransitionsDistinctlyFromAnEmptyButRanResult()
    {
        // Scenario ND-02: an unconfigured origin fails distinctly from a run that ran and found zero
        // opportunities. Unconfigured is reported as Failed with a structured, distinguishable reason.
        var unconfiguredRun = CreateValidRun();
        unconfiguredRun.Fail(NicheDiscoveryErrors.OriginUnconfigured.Code, Now.AddMinutes(1));

        var emptyButRanRun = CreateValidRun();
        emptyButRanRun.Start(Now.AddMinutes(1));
        emptyButRanRun.Complete(Now.AddMinutes(5));

        Assert.Equal(DiscoveryRunStatus.Failed, unconfiguredRun.Status);
        Assert.Equal(NicheDiscoveryErrors.OriginUnconfigured.Code, unconfiguredRun.FailureReasonKey);

        Assert.Equal(DiscoveryRunStatus.Completed, emptyButRanRun.Status);
        Assert.Null(emptyButRanRun.FailureReasonKey);
    }

    [Fact]
    public void Fail_QuotaOrDepthExhaustedMidRun_TransitionsToFailedWithStructuredReason()
    {
        var run = CreateValidRun();
        run.Start(Now.AddMinutes(1));

        run.Fail(NicheDiscoveryErrors.ProviderQuotaExhausted.Code, Now.AddMinutes(3));

        Assert.Equal(DiscoveryRunStatus.Failed, run.Status);
        Assert.Equal(NicheDiscoveryErrors.ProviderQuotaExhausted.Code, run.FailureReasonKey);
        Assert.Equal(Now.AddMinutes(3), run.CompletedAtUtc);
    }

    [Fact]
    public void Complete_FromRequested_ThrowsInvalidOperationException()
    {
        var run = CreateValidRun();

        Assert.Throws<InvalidOperationException>(() => run.Complete(Now.AddMinutes(1)));
    }

    /// <summary>
    /// A missing market code is operator input and comes back as a validation failure, not an
    /// exception — unlike the structural parameters below, which no caller can legitimately omit.
    /// </summary>
    [Theory]
    [InlineData(null, "es-AR")]
    [InlineData("es-AR", null)]
    public void Create_WithMissingMarketCode_FailsWithMarketCodeInvalid(string? sourceMarket, string? targetMarket)
    {
        var window = DiscoveryWindow.Create(30).Value;

        var result = DiscoveryRun.Create(
            OrganizationId, sourceMarket!, targetMarket!, "youtube", "long", window, DiscoveryDepth.Quick, "{}", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.MarketCodeInvalid, result.Error);
    }

    [Theory]
    [InlineData("es-AR", "es-AR", null, "long")]
    [InlineData("es-AR", "es-AR", "youtube", null)]
    public void Create_WithMissingRequiredParameter_Throws(
        string? sourceMarket, string? targetMarket, string? networkKey, string? contentFormat)
    {
        var window = DiscoveryWindow.Create(30).Value;

        Assert.ThrowsAny<ArgumentException>(() => DiscoveryRun.Create(
            OrganizationId,
            sourceMarket!,
            targetMarket!,
            networkKey!,
            contentFormat!,
            window,
            DiscoveryDepth.Quick,
            "{}",
            null,
            Now));
    }
}
