using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

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

    // Pre-change baseline instruction — verbatim. Byte-identical comparison required.
    // If this string changes, it is a BREAKING behavior change that MUST be intentional.
    private const string PreChangeBaselineInstruction =
        "Write ONE concise text-to-image prompt for a single carousel slide BACKGROUND image in this " +
        "client's established visual style. The background MUST be free of any text, letters, words, " +
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
    // Test infrastructure
    // -----------------------------------------------------------------------

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
