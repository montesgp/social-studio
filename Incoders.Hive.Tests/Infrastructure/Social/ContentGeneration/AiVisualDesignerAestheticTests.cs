using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Phase 2 (CAG2-CG-01/02): when a ClientAesthetic design context is supplied, the designer composes
/// creatives-first — it injects the client's style descriptors + reference creatives, and when the
/// client has NO marked palette it instructs the model to derive colour from the creatives WITHOUT
/// fabricating a default palette.
/// </summary>
public sealed class AiVisualDesignerAestheticTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task EmptyPalette_DerivesFromCreatives_AndDoesNotFabricatePalette()
    {
        var gateway = new CaptureGateway();
        var designer = new AiVisualDesigner(gateway, new StubResolver());
        var brand = BrandWithNoPalette();
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [new ProfilePostImageRef("p1", 0, "org/client/analysis/aesthetic/p1-0.jpg", "h1")],
            Palette: [],
            StyleDescriptors: ["editorial, high-contrast", "lots of negative space"],
            Motifs: ["hand-drawn arrows"],
            VariationSeed: "job-42");

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None, aesthetic);

        Assert.NotNull(gateway.CapturedInput);
        Assert.Contains("CLIENT-AESTHETIC MODE", gateway.CapturedInput!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("editorial, high-contrast", gateway.CapturedInput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("org/client/analysis/aesthetic/p1-0.jpg", gateway.CapturedInput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DO NOT invent", gateway.CapturedInput, StringComparison.OrdinalIgnoreCase);
        // Variation seed surfaced for variety-within-coherence.
        Assert.Contains("job-42", gateway.CapturedInput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReferenceImages_SentToDesignerAsVision_WhenStoragePresent()
    {
        var gateway = new CaptureGateway();
        var designer = new AiVisualDesigner(gateway, new StubResolver(), new OneImageRefStorage());
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages:
            [
                new ProfilePostImageRef("p1", 0, "org/c/a/aesthetic/p1-0.jpg", "h1"),
                new ProfilePostImageRef("p2", 0, "org/c/a/aesthetic/p2-0.jpg", "h2"),
            ],
            Palette: [],
            StyleDescriptors: ["editorial"],
            Motifs: [],
            VariationSeed: "job-1");

        await designer.ComposeAsync(OrgId, BuildSpec(), BrandWithNoPalette(), new BackgroundSelection.Creative(), CancellationToken.None, aesthetic);

        Assert.True(gateway.CapturedImageCount >= 2, "the client's reference images must be sent to the designer as vision");
    }

    [Fact]
    public async Task FeedDerivedPalette_LeadsOverEmptyBrandManual()
    {
        var gateway = new CaptureGateway();
        var designer = new AiVisualDesigner(gateway, new StubResolver());
        var brand = BrandWithNoPalette();
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#C0FFEE", "Primary", 1)],
            StyleDescriptors: ["warm, sunlit"],
            Motifs: [],
            VariationSeed: "job-7");

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None, aesthetic);

        Assert.NotNull(gateway.CapturedInput);
        Assert.Contains("#C0FFEE", gateway.CapturedInput!, StringComparison.OrdinalIgnoreCase);
    }

    private static BrandManualSnapshot BrandWithNoPalette() =>
        new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: new[] { "Inter" },
            VoiceAndTone: "Professional",
            Notes: null);

    private static VisualCompositionSpec BuildSpec() =>
        new VisualCompositionSpec(
            FormatKey: "story",
            LayoutType: "story-story",
            Palette: new CompositionPalette("#111111", "#222222", "#3B82F6", "#FFFFFF", "#F9FAFB"),
            Typography: new CompositionTypography("Inter", 24, "Inter", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
            Dimensions: new CompositionDimensions(1080, 1920, 96));
}

file sealed class OneImageRefStorage : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
        throw new NotSupportedException();
    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-image-bytes"));
        return Task.FromResult(Result.Success(new StoredAssetStream(stream, "image/jpeg", stream.Length)));
    }
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

file sealed class StubResolver : IWorkflowConfigurationResolver
{
    public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
        [
            new WorkflowStepRouteCandidate(Guid.NewGuid(), "claude", Guid.NewGuid(),
                IntegrationConnectionMode.BringYourOwnKey, 1, "claude-sonnet-4-6", null, true)
        ]));
}

file sealed class CaptureGateway : IAiCompletionGateway
{
    public string? CapturedInput { get; private set; }
    public int CapturedImageCount { get; private set; }

    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        CapturedInput = request.InputJson;
        CapturedImageCount = request.Images?.Count ?? 0;
        var json = JsonSerializer.Serialize(new
        {
            markup = """<div class="composition" data-format="story" style="width:1080px;height:1920px;"><div data-layer="background" style="background:#111;"></div><div data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3">Test</h1></div></div>"""
        });
        var payload = JsonSerializer.Deserialize<TResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(payload, "claude", "claude-sonnet-4-6", [], null)));
    }
}
