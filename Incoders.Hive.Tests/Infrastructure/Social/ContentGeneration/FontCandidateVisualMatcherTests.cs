using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using SkiaSharp;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// client-style-fidelity CSF-PA-02: the client's real text is compared against catalog candidates
/// rendered side by side, and any failure keeps the extractor's own choice.
/// </summary>
public sealed class FontCandidateVisualMatcherTests
{
    private static readonly BrandMarkBoundingBox Box = new(0.1, 0.2, 0.5, 0.3);

    [Fact]
    public void Sheet_RendersEveryCandidateWithTheClientsLineBreaks_BesideTheReference()
    {
        var request = Request(["League Gothic", "Antonio", "Anton"]) with
        {
            SampleText = "¿Sabés qué\ntoman\ntus clientes\nen tu local?",
        };

        var (markup, height) = FontCandidateVisualMatcher.BuildSheet(
            [(request, new FontCandidateVisualMatcher.SampleCrop("data:image/png;base64,AAAA", IsDark: true))]);

        Assert.Contains("data:image/png;base64,AAAA", markup);
        Assert.Contains("REFERENCE", markup);
        Assert.Contains("font-family:'League Gothic'", markup);
        Assert.Contains("font-family:'Anton'", markup);
        // Same line breaks, same light-on-dark scheme as the reference.
        Assert.Contains("toman<br>tus clientes", markup);
        Assert.Contains("color:#F2EDE2", markup);
        Assert.True(height > 0);
    }

    [Theory]
    [InlineData("uno\ndos\ntres", 3)]
    [InlineData("uno\\ndos", 2)]          // an escaped newline the model left in the JSON string
    [InlineData("una sola línea", 1)]
    public void SampleLines_FollowTheClientsBreaks(string sample, int expectedLines)
    {
        Assert.Equal(expectedLines, FontCandidateVisualMatcher.SampleLines(sample).Count);
    }

    [Fact]
    public async Task ChosenNumber_MapsToThatCandidate()
    {
        var gateway = new AnswerGateway("""{"heading": 2}""");
        var matcher = new FontCandidateVisualMatcher(new PngRenderer(), gateway);

        var chosen = await matcher.MatchAsync(
            Guid.NewGuid(), "openai", "gpt", [Request(["Anton", "League Gothic", "Oswald"])], CancellationToken.None);

        Assert.Equal("League Gothic", chosen[TypographyRoles.Heading]);
        // The sheet, then the original slide so the reference stays legible even with a loose crop.
        Assert.Equal(2, gateway.Images.Count);
        Assert.Equal("image/png", gateway.Images[1].MediaType);
    }

    [Theory]
    [InlineData("""{"heading": 0}""")]
    [InlineData("""{"heading": 9}""")]
    [InlineData("""{}""")]
    public async Task OutOfRangeOrMissingAnswer_KeepsTheExtractorChoice(string answer)
    {
        var matcher = new FontCandidateVisualMatcher(new PngRenderer(), new AnswerGateway(answer));

        var chosen = await matcher.MatchAsync(
            Guid.NewGuid(), "openai", "gpt", [Request(["Anton", "League Gothic"])], CancellationToken.None);

        Assert.Empty(chosen);
    }

    [Fact]
    public async Task RenderFailure_KeepsTheExtractorChoice_WithoutCallingTheModel()
    {
        var gateway = new AnswerGateway("""{"heading": 1}""");
        var matcher = new FontCandidateVisualMatcher(new FailingRenderer(), gateway);

        var chosen = await matcher.MatchAsync(
            Guid.NewGuid(), "openai", "gpt", [Request(["Anton", "League Gothic"])], CancellationToken.None);

        Assert.Empty(chosen);
        Assert.Empty(gateway.Images);
    }

    [Fact]
    public async Task SingleCandidate_IsNotCompared()
    {
        var gateway = new AnswerGateway("""{"heading": 1}""");
        var matcher = new FontCandidateVisualMatcher(new PngRenderer(), gateway);

        var chosen = await matcher.MatchAsync(
            Guid.NewGuid(), "openai", "gpt", [Request(["Anton"])], CancellationToken.None);

        Assert.Empty(chosen);
        Assert.Empty(gateway.Images);
    }

    [Fact]
    public void Crop_ReturnsAPngDataUri_DetectsTheGround_AndRejectsADegenerateBox()
    {
        var image = Png(400, 500);

        var crop = FontCandidateVisualMatcher.CropSample(image, Box);
        Assert.StartsWith("data:image/png;base64,", crop!.DataUri);
        Assert.True(crop.IsDark);
        Assert.Null(FontCandidateVisualMatcher.CropSample(image, new BrandMarkBoundingBox(0.99, 0.99, 0.001, 0.001)));
        Assert.Null(FontCandidateVisualMatcher.CropSample([1, 2, 3], Box));
    }

    private static FontMatchRequest Request(IReadOnlyList<string> candidates) =>
        new(TypographyRoles.Heading, candidates, "¿Sabés qué toman tus clientes en tu local?", Png(400, 500), Box);

    internal static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

file sealed class PngRenderer : IContentRenderer
{
    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct) =>
        Task.FromResult(Result.Success(RenderedAsset.Create(spec.FormatKey, "image/png", "sheet.png",
            FontCandidateVisualMatcherTests.Png(10, 10))));
}

file sealed class FailingRenderer : IContentRenderer
{
    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct) =>
        Task.FromResult(Result.Failure<RenderedAsset>(RenderErrors.RenderFailure));
}

file sealed class AnswerGateway(string answerJson) : IAiCompletionGateway
{
    public List<AiImageInput> Images { get; } = [];

    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        Images.AddRange(request.Images ?? []);
        var payload = JsonSerializer.Deserialize<TResponse>(answerJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(payload, "openai", "gpt", [], null)));
    }
}
