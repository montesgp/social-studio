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

    [Fact]
    public async Task ClientAesthetic_EmptyAnalysisPalette_DoesNotReceiveBrandManualColours()
    {
        // Owner-approved rule change: a ClientAesthetic job whose analysis captured NO palette must NOT
        // fall back to the brand-manual palette — the model already sees the real client creatives as
        // vision input, and a brand-manual palette would contradict that evidence. The DTO must carry the
        // (empty) analysis palette, not the brand-manual colours.
        var gateway = new CaptureGateway();
        var designer = new AiVisualDesigner(gateway, new StubResolver());
        var brand = BrandWithPalette();
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [new ProfilePostImageRef("p1", 0, "org/client/analysis/aesthetic/p1-0.jpg", "h1")],
            Palette: [],
            StyleDescriptors: ["editorial"],
            Motifs: [],
            VariationSeed: "job-99");

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None, aesthetic);

        Assert.NotNull(gateway.CapturedInput);
        // NOTE: the brand-manual palette hex may still appear elsewhere in the payload (e.g. the general
        // brand-notes prose in the system prompt is unconditional and out of this change's scope). What
        // this rule change controls is the DTO's `brandPalette` array — the actual colour input the
        // designer is told to use — which must stay EMPTY for a ClientAesthetic job with no captured
        // palette, not fall back to the brand's colours.
        Assert.Equal(0, ExtractBrandPaletteCount(gateway.CapturedInput!));
    }

    // ── D7: AllowedFonts merge — client classes supplement, never replace, the brand kit ──────

    [Fact]
    public async Task ClientTypography_Present_SupplementsAllowedFontsWithClientClasses_ClientFirst()
    {
        var gateway = new CaptureGateway();
        var designer = new AiVisualDesigner(gateway, new StubResolver());
        var brand = BrandWithNoPalette(); // Typography = ["Inter"]
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [],
            StyleDescriptors: ["editorial"],
            Motifs: [],
            VariationSeed: "job-8",
            Typography:
            [
                new AestheticTypography(TypographyRoles.Heading, "Playfair Display", 700, false, 0.08),
                new AestheticTypography(TypographyRoles.Body, "Poppins", 400, false, 0.03),
            ]);

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None, aesthetic);

        Assert.NotNull(gateway.CapturedInput);
        var allowedFonts = ExtractAllowedFonts(gateway.CapturedInput!);
        // Client-first ordering: the client's own (bundled) families, deduped case-insensitively,
        // THEN the brand kit appended.
        Assert.Equal(["Playfair Display", "Poppins", "Inter"], allowedFonts);
    }

    [Fact]
    public async Task ClientTypography_Null_AllowedFontsIsByteIdenticalToBrandTypographyAlone()
    {
        // Regression: a null/empty client typography set must never starve the designer — the
        // resulting array must equal today's `brand.Typography.ToArray()` exactly.
        var gateway = new CaptureGateway();
        var designer = new AiVisualDesigner(gateway, new StubResolver());
        var brand = BrandWithNoPalette(); // Typography = ["Inter"]
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [],
            StyleDescriptors: ["editorial"],
            Motifs: [],
            VariationSeed: "job-9",
            Typography: null);

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None, aesthetic);

        Assert.NotNull(gateway.CapturedInput);
        Assert.Equal(["Inter"], ExtractAllowedFonts(gateway.CapturedInput!));
    }

    [Fact]
    public async Task NonAestheticJob_AllowedFontsIsBrandTypographyAlone_Unchanged()
    {
        var gateway = new CaptureGateway();
        var designer = new AiVisualDesigner(gateway, new StubResolver());
        var brand = BrandWithNoPalette(); // Typography = ["Inter"]

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None, aesthetic: null);

        Assert.NotNull(gateway.CapturedInput);
        Assert.Equal(["Inter"], ExtractAllowedFonts(gateway.CapturedInput!));
    }

    /// <summary>Reads the top-level "allowedFonts" JSON array as a string list.</summary>
    private static IReadOnlyList<string> ExtractAllowedFonts(string capturedInputJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(capturedInputJson);
        return doc.RootElement.GetProperty("allowedFonts")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    [Fact]
    public async Task NonAestheticJob_StillReceivesBrandManualPalette()
    {
        // A non-aesthetic job (aesthetic is null — no client analysis to defer to) must keep the existing
        // brand-manual palette behaviour byte-for-byte.
        var gateway = new CaptureGateway();
        var designer = new AiVisualDesigner(gateway, new StubResolver());
        var brand = BrandWithPalette();

        await designer.ComposeAsync(OrgId, BuildSpec(), brand, new BackgroundSelection.Creative(), CancellationToken.None, aesthetic: null);

        Assert.NotNull(gateway.CapturedInput);
        Assert.Equal(1, ExtractBrandPaletteCount(gateway.CapturedInput!));
    }

    /// <summary>Reads the count of entries in the DTO's top-level "brandPalette" JSON array.</summary>
    private static int ExtractBrandPaletteCount(string capturedInputJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(capturedInputJson);
        return doc.RootElement.GetProperty("brandPalette").GetArrayLength();
    }

    private static BrandManualSnapshot BrandWithNoPalette() =>
        new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: new[] { "Inter" },
            VoiceAndTone: "Professional",
            Notes: null);

    private static BrandManualSnapshot BrandWithPalette() =>
        new BrandManualSnapshot(
            ColorPalette: [new WeightedColor("#06C4E5", ColorRole.Primary, 0)],
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
