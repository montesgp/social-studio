using System.Text;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;
using SocialStudio.Infrastructure.Process;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Infrastructure.Ai;

public sealed class ClaudePromptCliImageModelClientTests
{
    [Fact]
    public async Task GenerateAsync_ParsesMimeAndBase64Json()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var stdout = $"{{\"mime\":\"image/png\",\"base64\":\"{Convert.ToBase64String(bytes)}\"}}";
        var client = new ClaudePromptCliImageModelClient(BuildCredential(), new ImageCapturingProcessRunner(new ProcessRunResult(0, stdout, "", false)), NullLogger<ClaudePromptCliImageModelClient>.Instance);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(bytes, result.Value.Bytes);
        Assert.Equal("image/png", result.Value.Mime);
        Assert.Equal("claude-p", result.Value.ProviderKey);
    }

    [Fact]
    public async Task GenerateAsync_ParsesFilePathInsideAllowedOutputDirectory()
    {
        var dir = Directory.CreateTempSubdirectory("claude-p-image");
        var image = Path.Combine(dir.FullName, "out.png");
        await File.WriteAllBytesAsync(image, [9, 8, 7]);
        var stdout = $"{{\"mime\":\"image/png\",\"filePath\":\"{JsonEscape(image)}\"}}";
        var client = new ClaudePromptCliImageModelClient(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = "claude",
            ["allowed_output_directory"] = dir.FullName
        }), new ImageCapturingProcessRunner(new ProcessRunResult(0, stdout, "", false)), NullLogger<ClaudePromptCliImageModelClient>.Instance);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([9, 8, 7], result.Value.Bytes);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"mime\":\"image/png\"}")]
    public async Task GenerateAsync_InvalidJsonOrMissingBytesFails(string stdout)
    {
        var client = new ClaudePromptCliImageModelClient(BuildCredential(), new ImageCapturingProcessRunner(new ProcessRunResult(0, stdout, "", false)), NullLogger<ClaudePromptCliImageModelClient>.Instance);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GenerateAsync_RejectsFilePathOutsideAllowedOutputDirectory()
    {
        var allowed = Directory.CreateTempSubdirectory("claude-p-allowed");
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(outside, [1]);
        var stdout = $"{{\"mime\":\"image/png\",\"filePath\":\"{JsonEscape(outside)}\"}}";
        var client = new ClaudePromptCliImageModelClient(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = "claude",
            ["allowed_output_directory"] = allowed.FullName
        }), new ImageCapturingProcessRunner(new ProcessRunResult(0, stdout, "", false)), NullLogger<ClaudePromptCliImageModelClient>.Instance);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }


    [Fact]
    public async Task GenerateAsync_RejectsFilePathWhenImageFileExceedsConfiguredLimit()
    {
        var dir = Directory.CreateTempSubdirectory("claude-p-image-max");
        var image = Path.Combine(dir.FullName, "large.png");
        await File.WriteAllBytesAsync(image, [1, 2, 3, 4]);
        var stdout = $"{{\"mime\":\"image/png\",\"filePath\":\"{JsonEscape(image)}\"}}";
        var client = new ClaudePromptCliImageModelClient(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = "claude",
            ["allowed_output_directory"] = dir.FullName,
            ["max_image_file_bytes"] = "2"
        }), new ImageCapturingProcessRunner(new ProcessRunResult(0, stdout, "", false)), NullLogger<ClaudePromptCliImageModelClient>.Instance);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.AiJsonInvalid.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_TimeoutFailsWithProviderTimeout()
    {
        var client = new ClaudePromptCliImageModelClient(BuildCredential(), new ImageCapturingProcessRunner(new ProcessRunResult(-1, "", "", true)), NullLogger<ClaudePromptCliImageModelClient>.Instance);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderTimeout.Code, result.Error.Code);
    }

    private static ImageGenerationRequest BuildRequest() =>
        new(Guid.NewGuid(), "ai-image", "image/v1", "draw", 1024, 1024, "claude-p", "claude-sonnet-4-6");

    private static ResolvedProviderCredential BuildCredential(IReadOnlyDictionary<string, string>? values = null) =>
        new(Guid.Empty, "claude-p", IntegrationConnectionMode.BringYourOwnKey,
            values ?? new Dictionary<string, string> { ["executable_path"] = "claude" }, "claude-default");

    private static string JsonEscape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}


file sealed class ImageCapturingProcessRunner : IProcessRunner
{
    private readonly ProcessRunResult _result;
    public ProcessRunRequest? Request { get; private set; }
    public ImageCapturingProcessRunner(ProcessRunResult result) => _result = result;
    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        Request = request;
        return Task.FromResult(_result);
    }
}
