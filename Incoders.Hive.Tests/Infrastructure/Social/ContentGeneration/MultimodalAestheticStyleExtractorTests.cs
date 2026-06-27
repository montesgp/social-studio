using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>Phase 2 (CAG2-PA-03): multimodal extractor — palette optional; degrades when no route.</summary>
public sealed class MultimodalAestheticStyleExtractorTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly IReadOnlyList<ProfilePostImageRef> Refs =
        [new ProfilePostImageRef("p1", 0, "org/c/a/aesthetic/p1-0.jpg", "h1")];

    [Fact]
    public async Task NoConfiguredRoute_ReturnsEmpty_AndDoesNotCallGateway()
    {
        var gateway = new CaptureGateway();
        var extractor = new MultimodalAestheticStyleExtractor(gateway, EmptyResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Refs, [], CancellationToken.None);

        Assert.Empty(result.Palette);
        Assert.False(gateway.WasCalled);
    }

    [Fact]
    public async Task NoImages_ReturnsEmpty_WithoutResolvingRoute()
    {
        var gateway = new CaptureGateway();
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, [], ["caption"], CancellationToken.None);

        Assert.Same(AestheticExtraction.Empty, result);
        Assert.False(gateway.WasCalled);
    }

    [Fact]
    public async Task WithRouteAndImages_SendsVision_AndEmptyPaletteIsValid()
    {
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: """["editorial, high-contrast"]""", captionVoice: "playful");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Refs, ["summer drop"], CancellationToken.None);

        Assert.True(gateway.WasCalled);
        Assert.True(gateway.ImageCount > 0, "vision images must be sent to the gateway");
        Assert.Empty(result.Palette); // empty palette is a VALID result, not a degrade
        Assert.Contains("editorial, high-contrast", result.StyleDescriptors);
        Assert.Equal("playful", result.CaptionVoice);
    }

    [Fact]
    public async Task WithMarkedPalette_MapsRoleOrderedColours()
    {
        var gateway = new CaptureGateway(
            paletteJson: """[{"hex":"#C0FFEE","role":"Primary","rank":1}]""",
            descriptors: "[]",
            captionVoice: null);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Refs, [], CancellationToken.None);

        Assert.Single(result.Palette);
        Assert.Equal("#C0FFEE", result.Palette[0].Hex);
    }

    // ── WU-1 task 1.13 [RED] — slideMixPattern + colorPairings mapping ─────────

    [Fact]
    public async Task SlideMixPattern_Populated_MapsToBackgroundPattern()
    {
        // CAC-BSP-01-a: slideMixPattern → BackgroundPattern [Photo, FlatColor, Photo]
        var gateway = new CaptureGateway(
            paletteJson: "[]",
            descriptors: "[]",
            slideMixPattern: """["photo","flat","photo"]""");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Refs, [], CancellationToken.None);

        Assert.NotNull(result.BackgroundPattern);
        Assert.Equal(3, result.BackgroundPattern!.Count);
        Assert.Equal(SlideBackgroundKind.Photo, result.BackgroundPattern[0]);
        Assert.Equal(SlideBackgroundKind.FlatColor, result.BackgroundPattern[1]);
        Assert.Equal(SlideBackgroundKind.Photo, result.BackgroundPattern[2]);
    }

    [Fact]
    public async Task SlideMixPattern_Absent_BackgroundPatternIsNull_ExtractionCompletes()
    {
        // CAC-BSP-01-b: no slideMixPattern → BackgroundPattern null; completes successfully
        var gateway = new CaptureGateway(
            paletteJson: "[]",
            descriptors: "[]",
            slideMixPattern: null);
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Refs, [], CancellationToken.None);

        Assert.Null(result.BackgroundPattern);
        Assert.True(gateway.WasCalled);
    }

    [Fact]
    public async Task JoinablePalette_ProducesColorPairings_ViaDeriveCall()
    {
        // Given a palette with Primary + Neutral roles → ColorPairings derived
        var paletteJson = """[{"hex":"#0D0D0D","role":"Primary","rank":1},{"hex":"#FFFFFF","role":"Neutral","rank":2}]""";
        var gateway = new CaptureGateway(paletteJson: paletteJson, descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Refs, [], CancellationToken.None);

        Assert.NotNull(result.ColorPairings);
        Assert.NotEmpty(result.ColorPairings!);
        Assert.Equal("#0D0D0D", result.ColorPairings![0].BackgroundHex);
    }

    [Fact]
    public async Task EmptyPalette_ColorPairingsIsNullOrEmpty_ExtractionCompletes()
    {
        // Empty palette → no pairings; extraction still completes
        var gateway = new CaptureGateway(paletteJson: "[]", descriptors: "[]");
        var extractor = new MultimodalAestheticStyleExtractor(gateway, CandidateResolver(), new OneImageStorage());

        var result = await extractor.ExtractAsync(Org, Refs, [], CancellationToken.None);

        Assert.True(result.ColorPairings is null || result.ColorPairings.Count == 0);
    }

    private static IWorkflowConfigurationResolver EmptyResolver() => new StubResolver(candidate: false);
    private static IWorkflowConfigurationResolver CandidateResolver() => new StubResolver(candidate: true);
}

file sealed class StubResolver(bool candidate) : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WorkflowStepRouteCandidate> list = candidate
            ?
            [
                new WorkflowStepRouteCandidate(Guid.NewGuid(), "claude", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey, 1, "claude-opus-4-8", null, true)
            ]
            : [];
        return Task.FromResult(Result.Success(list));
    }
}

file sealed class OneImageStorage : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
        throw new NotSupportedException();
    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake-image-bytes"));
        return Task.FromResult(Result.Success(new StoredAssetStream(stream, "image/jpeg", stream.Length)));
    }
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

file sealed class CaptureGateway(
    string paletteJson = "[]",
    string descriptors = "[]",
    string? captionVoice = null,
    string? slideMixPattern = null)
    : IAiCompletionGateway
{
    public bool WasCalled { get; private set; }
    public int ImageCount { get; private set; }

    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        WasCalled = true;
        ImageCount = request.Images?.Count ?? 0;

        var voice = captionVoice is null ? "null" : $"\"{captionVoice}\"";
        var patternPart = slideMixPattern is null
            ? string.Empty
            : $",\"slideMixPattern\":{slideMixPattern}";
        var json = $$"""{"palette":{{paletteJson}},"styleDescriptors":{{descriptors}},"motifs":[],"captionVoice":{{voice}}{{patternPart}}}""";
        var payload = JsonSerializer.Deserialize<TResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(payload, "claude", "claude-opus-4-8", [], null)));
    }
}
