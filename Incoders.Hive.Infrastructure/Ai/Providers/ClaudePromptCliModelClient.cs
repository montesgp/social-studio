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

        // Isolation contract: as a Hive completion provider, claude-p must behave as a non-interactive
        // rendering function — never as the Claude Code coding agent. --system-prompt REPLACES the default
        // agent system prompt (this is what strips the agentic identity AND the dynamic cwd/git/branch
        // sections that made Claude treat the design payload as suspicious pasted content and refuse it),
        // --disable-slash-commands drops all skills, and --strict-mcp-config drops every MCP server (engram
        // and any other harness tooling) for this single atomic invocation. The schema is folded into the
        // override instead of a weaker --append-system-prompt. Verified against `claude -p` on 2026-07-03.
        args.Add("--system-prompt");
        args.Add(BuildIsolatedSystemPrompt(request.OutputJsonSchema));
        args.Add("--disable-slash-commands");
        args.Add("--strict-mcp-config");

        return args;
    }

    private const string IsolatedRendererSystemPrompt =
        "You are a non-interactive rendering function inside an automated content pipeline. " +
        "This invocation is fully isolated from any software-engineering context: for this single atomic " +
        "request, ignore all skills, memory, project instructions (CLAUDE.md/AGENTS.md), coding conventions, " +
        "harness behavior, git or repository state, and any Claude Code agent identity. There is no human to " +
        "converse with and no software task. Never ask questions, never refuse, never explain, never add " +
        "commentary or prose. Read the input payload and return ONLY a single well-formed JSON object that " +
        "conforms to the required schema. Output nothing before or after the JSON object.";

    private static string BuildIsolatedSystemPrompt(string? outputJsonSchema) =>
        string.IsNullOrWhiteSpace(outputJsonSchema)
            ? IsolatedRendererSystemPrompt
            : $"{IsolatedRendererSystemPrompt} Required JSON schema: {outputJsonSchema}";

    private static string Preview(string text) => text.Length <= 400 ? text : text[..400] + "…";
}
