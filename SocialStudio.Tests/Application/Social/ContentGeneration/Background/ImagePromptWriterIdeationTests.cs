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
/// WU-5 RED tests (task 5.1): ImagePromptWriter builds method-gated ideation instructions
/// when ConceptualMethod is present and non-Unknown, and appends ratio-aware cues when
/// ImageTypeRatio is present. Cold-start parity: null + null → instruction byte-identical to
/// the pre-change emulate-style baseline.
///
/// Refs: CACD-CG-01-a/b/c/d, CACD-CG-02-a/b/c, CACD-CG-03-a,
///       CACD-IMG-01-a/b/c, CACD-IMG-02-a; design §3.4, OQ-5; tasks 5.1.
/// </summary>
public sealed class ImagePromptWriterIdeationTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    // Baseline instruction — verbatim from the cold-start emulate-style branch, updated deliberately
    // for the topical-fidelity instruction (CACD-IMG-02, content-layout-and-image-fidelity WU-1).
    // Any change here MUST be intentional and reflected in the implementation.
    internal const string BaselineInstruction =
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
    // Atlas fixture: MetaphoricalEditorial (CACD-CG-01-a; Atlas exemplar)
    // -----------------------------------------------------------------------
    private static ConceptualMethod AtlasMethod() => new(
        Method: ArtDirectionMethod.MetaphoricalEditorial,
        Rationale: "Message becomes a visual metaphor through mono-color still-life staging",
        Cues: ["mono-color backdrop", "object-as-metaphor"]);

    // -----------------------------------------------------------------------
    // Comet fixture: LiteralIllustration (CACD-CG-01-b; Comet exemplar)
    // -----------------------------------------------------------------------
    private static ConceptualMethod CometMethod() => new(
        Method: ArtDirectionMethod.LiteralIllustration,
        Rationale: "Literal tender watercolor illustration of the subject",
        Cues: ["tender watercolor", "children characters"]);

    private static ClientAestheticProfile BaseProfile() => new(
        BackgroundDescriptors: ["clean studio backdrop", "minimal props"],
        VoiceTone: "inspiring",
        AverageSlidesPerCarousel: 5.0,
        AnalyzedAtUtc: DateTime.UtcNow);

    // -----------------------------------------------------------------------
    // 5.1-a: MetaphoricalEditorial → instruction contains method-specific ideation content;
    //         no @ (no handle directives). (CACD-CG-01-a; CACD-IMG-01-a)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_MetaphoricalEditorial_ProducesIdeationInstruction()
    {
        var aesthetic = BaseProfile() with { ConceptualMethod = AtlasMethod() };
        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, "Unlock the power of nature", aesthetic,
                SlideKind: SlideBackgroundKind.Photo));

        Assert.Contains("MetaphoricalEditorial", instruction, StringComparison.Ordinal);
        Assert.Contains("mono-color backdrop", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("object-as-metaphor", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", instruction);
    }

    // -----------------------------------------------------------------------
    // 5.1-b: LiteralIllustration → instruction contains literal/tender-scene content;
    //         no @ (no handle directives). (CACD-CG-01-b; CACD-IMG-01-b)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_LiteralIllustration_ProducesIdeationInstruction()
    {
        var aesthetic = BaseProfile() with { ConceptualMethod = CometMethod() };
        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, "Family moments to treasure", aesthetic,
                SlideKind: SlideBackgroundKind.Photo));

        Assert.Contains("LiteralIllustration", instruction, StringComparison.Ordinal);
        Assert.Contains("tender watercolor", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("children characters", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", instruction);
    }

    // -----------------------------------------------------------------------
    // 5.1-c: Same slide topic, Atlas vs Comet → instructions are discernibly different.
    //         (CACD-CG-01-c; CACD-IMG-01-c)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_SameSlide_AtlasVsComet_ProduceDifferentInstructions()
    {
        const string SlideTopic = "Summer is the season of growth";

        var atlasProfile = BaseProfile() with { ConceptualMethod = AtlasMethod() };
        var cometProfile = BaseProfile() with { ConceptualMethod = CometMethod() };

        var atlasInstruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, SlideTopic, atlasProfile,
                SlideKind: SlideBackgroundKind.Photo));
        var cometInstruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, SlideTopic, cometProfile,
                SlideKind: SlideBackgroundKind.Photo));

        Assert.NotEqual(atlasInstruction, cometInstruction);
    }

    // -----------------------------------------------------------------------
    // 5.1-d: ConceptualMethod = null → NO ideation section; instruction byte-identical
    //         to the pre-change emulate-style baseline. (CACD-CG-01-d; CACD-CG-03-a; CACD-PA-07-a)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_NullConceptualMethod_ProducesBaselineInstruction()
    {
        var aesthetic = BaseProfile(); // no ConceptualMethod
        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, "Slide copy", aesthetic));

        Assert.Equal(BaselineInstruction, instruction);
    }

    // -----------------------------------------------------------------------
    // 5.1-e: ArtDirectionMethod.Unknown → treated as null; emulate-style instruction unchanged.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_UnknownMethod_ProducesBaselineInstruction()
    {
        var unknownMethod = new ConceptualMethod(
            Method: ArtDirectionMethod.Unknown,
            Rationale: "unknown",
            Cues: []);
        var aesthetic = BaseProfile() with { ConceptualMethod = unknownMethod };
        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, "Slide copy", aesthetic,
                SlideKind: SlideBackgroundKind.Photo));

        Assert.Equal(BaselineInstruction, instruction);
    }

    // -----------------------------------------------------------------------
    // 5.1-f: ImageTypeRatio non-null + Photo slide → instruction includes photographic cue.
    //         (CACD-CG-02-a)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_NonNullRatio_PhotoSlide_IncludesPhotographicCue()
    {
        var ratio = new ImageTypeRatio(Photo: 0.6, FlatColor: 0.3, Decorator: 0.1, SampleSize: 10);
        var aesthetic = BaseProfile() with { ImageTypeRatio = ratio };
        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, "Slide copy", aesthetic,
                SlideKind: SlideBackgroundKind.Photo));

        Assert.Contains("photographic", instruction, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // 5.1-g: ImageTypeRatio non-null + FlatColor slide → instruction includes flat/mono-color cue.
    //         (CACD-CG-02-b)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_NonNullRatio_FlatColorSlide_IncludesFlatCue()
    {
        var ratio = new ImageTypeRatio(Photo: 0.1, FlatColor: 0.8, Decorator: 0.1, SampleSize: 10);
        var aesthetic = BaseProfile() with { ImageTypeRatio = ratio };
        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, "Slide copy", aesthetic,
                SlideKind: SlideBackgroundKind.FlatColor));

        Assert.Contains("flat mono-color", instruction, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // 5.1-h: ImageTypeRatio = null → no ratio cue; instruction = baseline (CACD-CG-02-c)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_NullRatio_NoRatioCueAdded_InstructionUnchanged()
    {
        var aesthetic = BaseProfile(); // ImageTypeRatio = null
        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, "Slide copy", aesthetic,
                SlideKind: SlideBackgroundKind.Photo));

        Assert.Equal(BaselineInstruction, instruction);
    }

    // -----------------------------------------------------------------------
    // 5.1-i: Null ConceptualMethod + null ImageTypeRatio → instruction byte-identical to baseline.
    //         (CACD-CG-03-a; parity cross-check for 5.3)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BuildInput_NullConceptualMethodAndNullRatio_ProducesBaselineInstruction()
    {
        var aesthetic = BaseProfile(); // both null
        var instruction = await CaptureInstructionAsync(
            new ImagePromptRequest(OrgId, ClientId, 1, "Slide copy", aesthetic));

        Assert.Equal(BaselineInstruction, instruction);
    }

    // -----------------------------------------------------------------------
    // Test infrastructure
    // -----------------------------------------------------------------------

    private static async Task<string> CaptureInstructionAsync(ImagePromptRequest request)
    {
        string? capturedJson = null;
        var gateway = new InstructionCaptureGateway(json => capturedJson = json);
        var writer = new ImagePromptWriter(
            gateway,
            FakeResolverIdeation.Default(),
            NullLogger<ImagePromptWriter>.Instance);

        var result = await writer.WriteAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess, $"WriteAsync failed: {(result.IsFailure ? result.Error.Code : "n/a")}");

        using var doc = JsonDocument.Parse(capturedJson!);
        return doc.RootElement.GetProperty("instruction").GetString()
               ?? throw new InvalidOperationException("instruction field missing or null in BuildInput JSON");
    }

    private sealed class InstructionCaptureGateway : IAiCompletionGateway
    {
        private readonly Action<string> _capture;
        public InstructionCaptureGateway(Action<string> capture) => _capture = capture;

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            _capture(request.InputJson);

            const string ResponseJson = """{"imagePrompt":"ideation-test-prompt"}""";
            var payload = JsonSerializer.Deserialize<TResponse>(
                ResponseJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (payload is null)
                return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                    Error.Failure("test-deserialize-failed", "test.deserialize.failed")));

            return Task.FromResult(Result.Success(
                new AiJsonCompletionResponse<TResponse>(payload, "test-provider", "test-model", [])));
        }
    }

    private sealed class FakeResolverIdeation : IWorkflowConfigurationResolver
    {
        private FakeResolverIdeation() { }
        public static FakeResolverIdeation Default() => new();

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
