using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Process;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

public sealed class ClaudePromptCliModelClient : IAiModelClient
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<ClaudePromptCliModelClient> _logger;

    public ClaudePromptCliModelClient(
        ResolvedProviderCredential credential,
        IProcessRunner processRunner,
        ILogger<ClaudePromptCliModelClient>? logger = null)
    {
        _credential = credential;
        _processRunner = processRunner;
        _logger = logger ?? NullLogger<ClaudePromptCliModelClient>.Instance;
    }

    public async Task<Result<AiCompletionResponse>> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var settings = ClaudePromptCliSettings.FromCredential(_credential);
        if (settings.IsFailure)
        {
            return Result.Failure<AiCompletionResponse>(settings.Error);
        }

        var model = request.PreferredModel ?? _credential.SelectedModel ?? settings.Value.DefaultModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            return Result.Failure<AiCompletionResponse>(IntegrationErrors.AiModelRequired);
        }

        var result = await _processRunner.RunAsync(new ProcessRunRequest(
            settings.Value.ExecutablePath,
            BuildArguments(request, settings.Value, model),
            settings.Value.WorkingDirectory,
            settings.Value.Timeout), cancellationToken);

        if (result.TimedOut)
        {
            _logger.LogWarning("Claude -p completion timed out. OrganizationId={OrganizationId} Model={Model}", request.OrganizationId, model);
            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ProviderTimeout);
        }

        if (result.OutputLimitExceeded)
        {
            _logger.LogWarning("Claude -p completion output exceeded configured capture limit. OrganizationId={OrganizationId} Model={Model}", request.OrganizationId, model);
            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ExternalProviderUnavailable);
        }

        if (result.ExitCode != 0)
        {
            _logger.LogWarning("Claude -p completion failed. OrganizationId={OrganizationId} Model={Model} ExitCode={ExitCode} Stderr={Stderr}", request.OrganizationId, model, result.ExitCode, Preview(result.StandardError));
            return Result.Failure<AiCompletionResponse>(IntegrationErrors.ExternalProviderUnavailable);
        }

        return Result.Success(new AiCompletionResponse(
            result.StandardOutput.Trim(),
            ClaudePromptCliSettings.ProviderKey,
            model,
            []));
    }

    internal static IReadOnlyList<string> BuildArguments(AiCompletionRequest request, ClaudePromptCliSettings settings, string model)
    {
        var args = new List<string> { "-p", request.Input };
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("--model");
            args.Add(model);
        }

        if (string.Equals(settings.OutputFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--output-format");
            args.Add("json");
        }

        if (!string.IsNullOrWhiteSpace(settings.PermissionMode))
        {
            args.Add("--permission-mode");
            args.Add(settings.PermissionMode);
        }

        foreach (var tool in settings.AllowedTools)
        {
            args.Add("--allowedTools");
            args.Add(tool);
        }

        if (!string.IsNullOrWhiteSpace(request.OutputJsonSchema))
        {
            args.Add("--append-system-prompt");
            args.Add($"Return output that conforms to this JSON schema: {request.OutputJsonSchema}");
        }

        return args;
    }

    private static string Preview(string text) => text.Length <= 400 ? text : text[..400] + "…";
}
