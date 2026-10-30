using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Background;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// WU-5 PARITY test (task 5.3, Definition-of-Done): asserts that
/// <c>ImagePromptWriter</c> with no conceptual data
/// (<c>ConceptualMethod = null</c>, <c>ImageTypeRatio = null</c>)
/// produces an instruction text BYTE-IDENTICAL to the pre-change emulate-style baseline
/// for the same <c>ClientAestheticProfile</c> and slide inputs.
///
/// Scope: INSTRUCTION TEXT only (not LLM output).
/// Refs: CACD-PA-07-a, CACD-IMG-02-a, CACD-CG-03-a; tasks 5.3.
/// </summary>
public sealed class ImagePromptWriterColdStartParityTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    // Cold-start baseline instruction — verbatim, updated deliberately for the topical-fidelity
    // instruction (CACD-IMG-02, content-layout-and-image-fidelity WU-1). Byte-identical comparison
    // required. If this string changes again, it is a BREAKING behavior change that MUST be intentional.
    private const string PreChangeBaselineInstruction =
        "Write ONE concise text-to-image prompt for a single carousel slide BACKGROUND image in this " +
        "client's established visual style. " +
        "The image MUST depict the subject matter of THIS slide's message as given in slideCopySummary — " +
        "a literal or metaphorical scene of the message subjects — rendered in the client's established " +
        "visual style. " +
        "The background MUST be free of any text, letters, words, " +
        "numbers, logos, handles, or watermarks — copy is composited separately as an editable layer. " +
        "Emulate the client's real aesthetic (composition, palette, mood, motifs) so the result looks " +
        "authentically authored by this client, never generic. Keep it vivid but concise. " +
        "Respond with ONLY raw JSON matching the expected structure.";

    // -----------------------------------------------------------------------
    // PARITY: full cold-start profile (null ConceptualMethod + null ImageTypeRatio)
    //         → instruction text BYTE-IDENTICAL to pre-change baseline.
    //         No ideation section. No ratio section.
    //         (CACD-PA-07-a; CACD-IMG-02-a; CACD-CG-03-a)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_NullConceptualMethod_NullImageTypeRatio_InstructionByteIdenticalToBaseline()
    {
        // A rich profile without any conceptual detection fields — cold-start shape.
        var coldStartProfile = new ClientAestheticProfile(
            BackgroundDescriptors: ["dark botanical", "hand-drawn textures"],
            VoiceTone: "warm and approachable",
            AverageSlidesPerCarousel: 5.0,
            AnalyzedAtUtc: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            StyleDescriptors: ["organic", "earthy", "natural light"],
            Motifs: ["botanical leaves", "wooden textures"],
            CaptionVoice: "educational yet friendly",
            Palette:
            [
                new AestheticPaletteEntry("#3D2B1F", "primary", 0),
                new AestheticPaletteEntry("#A8C5A0", "accent", 1),
            ],
            // ConceptualMethod and ImageTypeRatio default to null — cold-start state.
            ConceptualMethod: null,
            ImageTypeRatio: null);

        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(
                OrgId, ClientId,
                SlideIndex: 1,
                SlideCopySummary: "Discover the power of natural ingredients",
                Aesthetic: coldStartProfile));

        // Byte-identical comparison: no ideation section, no ratio section.
        Assert.Equal(PreChangeBaselineInstruction, instruction);
    }

    [Fact]
    public async Task BuildInput_ColdStartProfile_NoIdeationSection_NoRatioSection()
    {
        var coldStartProfile = new ClientAestheticProfile(
            BackgroundDescriptors: ["studio backdrop"],
            VoiceTone: "inspiring",
            AverageSlidesPerCarousel: 6.0,
            AnalyzedAtUtc: DateTime.UtcNow);

        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 2, "Build your future today", coldStartProfile));

        Assert.DoesNotContain("INVENT", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("conceptualMethod", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("imageTypeRatio", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("photographic", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flat mono-color", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PreChangeBaselineInstruction, instruction);
    }

    // -----------------------------------------------------------------------
    // 1.2 (CACD-IMG-02-b): aesthetic guidance (style descriptors, motifs, background descriptors,
    //     voice tone, palette) is still present in the assembled JSON and not displaced by the new
    //     topical-fidelity instruction sentence.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_ColdStartProfile_AestheticGuidanceStillPresent()
    {
        var coldStartProfile = new ClientAestheticProfile(
            BackgroundDescriptors: ["dark botanical", "hand-drawn textures"],
            VoiceTone: "warm and approachable",
            AverageSlidesPerCarousel: 5.0,
            AnalyzedAtUtc: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            StyleDescriptors: ["organic", "earthy", "natural light"],
            Motifs: ["botanical leaves", "wooden textures"],
            CaptionVoice: "educational yet friendly",
            Palette:
            [
                new AestheticPaletteEntry("#3D2B1F", "primary", 0),
                new AestheticPaletteEntry("#A8C5A0", "accent", 1),
            ],
            ConceptualMethod: null,
            ImageTypeRatio: null);

        var capturedJson = await CaptureFullJsonAsync(
            new ImagePromptRequest(
                OrgId, ClientId,
                SlideIndex: 1,
                SlideCopySummary: "Discover the power of natural ingredients",
                Aesthetic: coldStartProfile));

        Assert.Contains("organic", capturedJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("botanical leaves", capturedJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dark botanical", capturedJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warm and approachable", capturedJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#3D2B1F", capturedJson, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(capturedJson);
        var instruction = doc.RootElement.GetProperty("instruction").GetString();
        Assert.Contains("slideCopySummary", instruction, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Test infrastructure
    // -----------------------------------------------------------------------

    private static async Task<string> CaptureFullJsonAsync(ImagePromptRequest request)
    {
        string? capturedJson = null;
        var gateway = new JsonCaptureGateway(json => capturedJson = json);
        var writer = new ImagePromptWriter(
            gateway,
            FakeParityResolver.Default(),
            NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess, $"WriteAsync must succeed; got: {(result.IsFailure ? result.Error.Code : "n/a")}");

        return capturedJson!;
    }

    private static async Task<string> CaptureInstructionAsync(ImagePromptRequest request)
    {
        string? capturedJson = null;
        var gateway = new JsonCaptureGateway(json => capturedJson = json);
        var writer = new ImagePromptWriter(
            gateway,
            FakeParityResolver.Default(),
            NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess, $"WriteAsync must succeed; got: {(result.IsFailure ? result.Error.Code : "n/a")}");

        using var doc = JsonDocument.Parse(capturedJson!);
        return doc.RootElement.GetProperty("instruction").GetString()
               ?? throw new InvalidOperationException("instruction field missing in BuildInput JSON");
    }

    private sealed class JsonCaptureGateway : IAiCompletionGateway
    {
        private readonly Action<string> _capture;
        public JsonCaptureGateway(Action<string> capture) => _capture = capture;

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            _capture(request.InputJson);
            const string Json = """{"imagePrompt":"parity-test-prompt"}""";
            var payload = JsonSerializer.Deserialize<TResponse>(
                Json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (payload is null)
                return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                    Error.Failure("parity-deserialize-failed", "parity.deserialize.failed")));
            return Task.FromResult(Result.Success(
                new AiJsonCompletionResponse<TResponse>(payload, "parity-provider", "parity-model", [])));
        }
    }

    private sealed class FakeParityResolver : IWorkflowConfigurationResolver
    {
        private FakeParityResolver() { }
        public static FakeParityResolver Default() => new();

        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WorkflowStepRouteCandidate> candidates =
            [
                new WorkflowStepRouteCandidate(
                    Guid.NewGuid(), "openai", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey,
                    1, "gpt-4o", null, true),
            ];
            return Task.FromResult(Result.Success(candidates));
        }
    }
}
