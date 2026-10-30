using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Process;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Ai.Providers;

public sealed class ClaudePromptCliImageModelClient : IImageModelClient
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<ClaudePromptCliImageModelClient> _logger;

    /// <summary>
    /// The Claude -p CLI adapter generates images via the Claude model's multimodal output but
    /// does not currently support multi-image reference conditioning via ReferenceImages.
    /// Refs: CAC-IMG-01; design §3.4.
    /// </summary>
    public ImageModelCapabilities Capabilities { get; } =
        new(SupportsImageConditioning: false, MaxConditioningImages: 0);

    public ClaudePromptCliImageModelClient(
        ResolvedProviderCredential credential,
        IProcessRunner processRunner,
        ILogger<ClaudePromptCliImageModelClient>? logger = null)
    {
        _credential = credential;
        _processRunner = processRunner;
        _logger = logger ?? NullLogger<ClaudePromptCliImageModelClient>.Instance;
    }

    public async Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        var settings = ClaudePromptCliSettings.FromCredential(_credential);
        if (settings.IsFailure)
        {
            return Result.Failure<ImageGenerationResult>(settings.Error);
        }

        var model = request.PreferredModel ?? _credential.SelectedModel ?? settings.Value.DefaultModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiModelRequired);
        }

        var cliRequest = new AiCompletionRequest(
            request.OrganizationId,
            request.CapabilityKey,
            request.PromptVersionKey,
            request.Prompt,
            request.PreferredProviderKey,
            model,
            OutputJsonSchema: "{\"type\":\"object\",\"required\":[\"mime\"],\"properties\":{\"mime\":{\"type\":\"string\"},\"base64\":{\"type\":\"string\"},\"filePath\":{\"type\":\"string\"}}}");

        var result = await _processRunner.RunAsync(new ProcessRunRequest(
            settings.Value.ExecutablePath,
            ClaudePromptCliModelClient.BuildArguments(cliRequest, settings.Value with { OutputFormat = "json" }, model),
            settings.Value.WorkingDirectory,
            settings.Value.Timeout), ct);

        if (result.TimedOut)
        {
            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ProviderTimeout);
        }

        if (result.OutputLimitExceeded)
        {
            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnavailable);
        }

        if (result.ExitCode != 0)
        {
            _logger.LogWarning("Claude -p image generation failed. ExitCode={ExitCode} Stderr={Stderr}", result.ExitCode, result.StandardError);
            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnavailable);
        }

        var image = TryParseImage(result.StandardOutput, settings.Value.AllowedOutputDirectory, settings.Value.MaxImageFileBytes);
        if (image is null)
        {
            return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiJsonInvalid);
        }

        return Result.Success(new ImageGenerationResult(image.Value.Bytes, image.Value.Mime, ClaudePromptCliSettings.ProviderKey, model, new ImageUsage(1, image.Value.Bytes.Length)));
    }

    private static ParsedImage? TryParseImage(string stdout, string? allowedOutputDirectory, int maxImageFileBytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (!root.TryGetProperty("mime", out var mimeElement))
            {
                return null;
            }

            var mime = mimeElement.GetString();
            if (string.IsNullOrWhiteSpace(mime))
            {
                return null;
            }

            if (root.TryGetProperty("base64", out var base64Element))
            {
                var base64 = base64Element.GetString();
                return string.IsNullOrWhiteSpace(base64) ? null : new ParsedImage(Convert.FromBase64String(base64), mime);
            }

            if (root.TryGetProperty("filePath", out var filePathElement))
            {
                var filePath = filePathElement.GetString();
                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(allowedOutputDirectory))
                {
                    return null;
                }

                var fullPath = Path.GetFullPath(filePath);
                var allowed = Path.GetFullPath(allowedOutputDirectory);
                if (!fullPath.StartsWith(allowed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (!File.Exists(fullPath))
                {
                    return null;
                }

                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length > maxImageFileBytes)
                {
                    return null;
                }

                return new ParsedImage(File.ReadAllBytes(fullPath), mime);
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private readonly record struct ParsedImage(byte[] Bytes, string Mime);
}
