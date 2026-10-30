using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration;

/// <summary>
/// B1a TDD tests for the IVisualDesigner Application port contract.
/// Spec: VD-01 — IVisualDesigner Application Port Exists.
/// Tasks: 4.1
/// </summary>
public sealed class IVisualDesignerContractTests
{
    // -----------------------------------------------------------------------
    // VD-01 Scenario 1: Port has no Infrastructure types
    // -----------------------------------------------------------------------

    [Fact]
    public void Port_HasNoInfrastructureTypes()
    {
        // GIVEN the compiled Application assembly
        var appAssembly = typeof(IVisualDesigner).Assembly;

        // WHEN its exported types are inspected
        var designerType = appAssembly.GetExportedTypes()
            .FirstOrDefault(t => t.Name == "IVisualDesigner");

        // THEN IVisualDesigner is present as a public interface
        Assert.NotNull(designerType);
        Assert.True(designerType!.IsInterface);

        // AND IsActive capability discriminator exists on the interface
        var isActiveProperty = designerType.GetProperty("IsActive");
        Assert.NotNull(isActiveProperty);
        Assert.Equal(typeof(bool), isActiveProperty!.PropertyType);

        // AND no Infrastructure/persistence namespace appears in method parameter/return types
        var method = designerType.GetMethod("ComposeAsync");
        Assert.NotNull(method);

        var allTypes = method!.GetParameters()
            .Select(p => p.ParameterType)
            .Append(method.ReturnType)
            .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : new[] { t })
            .ToList();

        foreach (var type in allTypes)
        {
            Assert.DoesNotMatch(@"Infrastructure|Persistence|EfCore|Npgsql|Supabase|AspNetCore|Playwright",
                type.FullName ?? string.Empty);
        }
    }

    // -----------------------------------------------------------------------
    // VD-01 Scenario 2: ComposeAsync stub returns non-empty PromptVersionKey
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ComposeAsync_Stub_ReturnsNonEmptyPromptVersionKey()
    {
        // GIVEN a stubbed IVisualDesigner
        IVisualDesigner designer = new StubVisualDesigner();

        var spec = new VisualCompositionSpec(
            FormatKey: "story",
            LayoutType: "story-story",
            Palette: new CompositionPalette("#FFFFFF", "#000000", "#0055FF", "#333333", "#F9FAFB"),
            Typography: new CompositionTypography("Inter", 32, "Roboto", 16),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test headline" },
            Dimensions: new CompositionDimensions(1080, 1920, 96));

        var brand = new BrandManualSnapshot(
            ColorPalette: new[] { new WeightedColor("#FFFFFF", null, 0), new WeightedColor("#000000", null, 1), new WeightedColor("#0055FF", null, 2) },
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: new[] { "Inter", "Roboto" },
            VoiceAndTone: null,
            Notes: null);

        var selection = new BackgroundSelection.Creative();

        // WHEN ComposeAsync is called with valid inputs
        var result = await designer.ComposeAsync(
            Guid.NewGuid(), spec, brand, selection, CancellationToken.None);

        // THEN Result is successful and PromptVersionKey is a non-null non-empty string
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.PromptVersionKey);
        Assert.NotEmpty(result.Value.PromptVersionKey);
    }
}

/// <summary>
/// Stub IVisualDesigner for tests — returns fixed output with no real LLM call.
/// PromptVersionKey is the same deterministic constant as the design specifies.
/// </summary>
file sealed class StubVisualDesigner : IVisualDesigner
{
    public const string PromptVersionKey = "visual-designer/v1";

    /// <inheritdoc />
    public bool IsActive => true; // Active test stub — participates in the designer stage

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken,
        SocialStudio.Application.Features.Social.ContentGeneration.Shared.ClientAestheticDesignContext? aesthetic = null)
    {
        var output = new VisualDesignOutput(
            Markup: BuildStubMarkup(),
            PromptVersionKey: PromptVersionKey,
            IsValid: false);

        return Task.FromResult(Result.Success(output));
    }

    internal static string BuildStubMarkup(
        string? extraStyle = null,
        string? headlineText = null,
        string? fontFamily = null)
    {
        var ffStyle = fontFamily is not null ? $"font-family:{fontFamily};" : "font-family:Inter;";
        var extra = extraStyle ?? string.Empty;
        var headline = headlineText ?? "Hello World";
        return $"""
            <div class="composition" data-format="story" style="width:1080px;height:1920px;{extra}">
              <div class="layer-background" data-layer="background" style="background:#000000;"></div>
              <div class="layer-content" data-layer="content">
                <h1 class="slot" data-slot="headline" data-max-lines="3" style="{ffStyle}color:#FFFFFF;">{headline}</h1>
              </div>
            </div>
            """;
    }
}
