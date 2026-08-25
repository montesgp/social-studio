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
    private readonly string _visionStagingRoot;

    public ClaudePromptCliModelClient(
        ResolvedProviderCredential credential,
        IProcessRunner processRunner,
        ILogger<ClaudePromptCliModelClient>? logger = null,
        string? visionStagingRoot = null)
    {
        _credential = credential;
        _processRunner = processRunner;
        _logger = logger ?? NullLogger<ClaudePromptCliModelClient>.Instance;
        _visionStagingRoot = string.IsNullOrWhiteSpace(visionStagingRoot)
            ? Path.Combine(Path.GetTempPath(), "hive-claude-p-vision")
            : visionStagingRoot;
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

        // Vision input: the CLI has no request body that can carry base64 image blocks, so images are
        // staged to disk and handed over as paths the `Read` tool opens. Empty when the request has no
        // inline image bytes, in which case everything below is byte-identical to the text-only path.
        var staged = StageVisionImages(request.Images);

        try
        {
            var result = await _processRunner.RunAsync(new ProcessRunRequest(
                settings.Value.ExecutablePath,
                BuildArguments(request, settings.Value, model, staged.Count > 0),
                settings.Value.WorkingDirectory,
                settings.Value.Timeout,
                // The prompt is delivered via stdin (not as a `-p <prompt>` argument) so large prompts
                // don't blow the Windows ~32 KB command-line limit (Win32 206). See issue #69.
                StandardInput: BuildStandardInput(request.Input, staged)), cancellationToken);

            return MapResult(result, request, model);
        }
        finally
        {
            CleanupVisionStaging(staged);
        }
    }

    private Result<AiCompletionResponse> MapResult(ProcessRunResult result, AiCompletionRequest request, string model)
    {
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

    // ── Vision staging ───────────────────────────────────────────────────────
    // `claude -p` cannot receive base64 image blocks the way the HTTP API adapter can
    // (ClaudeModelClient.BuildMultimodalContent). It CAN see an image when handed a filesystem path
    // plus the Read tool — verified against `claude -p --allowedTools Read` on 2026-08-21, for paths
    // both inside and outside the working directory. Without this, AiCompletionRequest.Images was
    // silently dropped and the model answered from captions alone, inventing brand palettes it had
    // never seen.

    private IReadOnlyList<StagedVisionImage> StageVisionImages(IReadOnlyList<AiImageInput>? images)
    {
        if (images is not { Count: > 0 })
        {
            return [];
        }

        // Url-only inputs cannot be staged (the CLI has no fetcher); they degrade to text-only
        // rather than failing the completion.
        var withBytes = images.Where(i => !string.IsNullOrWhiteSpace(i.Base64Data)).ToList();
        if (withBytes.Count == 0)
        {
            return [];
        }

        var directory = Path.Combine(_visionStagingRoot, Guid.NewGuid().ToString("N"));
        var staged = new List<StagedVisionImage>(withBytes.Count);

        try
        {
            Directory.CreateDirectory(directory);

            for (var i = 0; i < withBytes.Count; i++)
            {
                var image = withBytes[i];
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(image.Base64Data!);
                }
                catch (FormatException)
                {
                    _logger.LogWarning(
                        "Claude -p vision: image {Index} carried malformed base64 and was skipped.", i);
                    continue;
                }

                var path = Path.Combine(directory, $"reference-{i + 1:D2}{ExtensionFor(image.MediaType)}");
                File.WriteAllBytes(path, bytes);
                staged.Add(new StagedVisionImage(path, directory));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Staging is best-effort: a failure degrades this call to text-only instead of failing it.
            _logger.LogWarning(ex, "Claude -p vision: could not stage reference images under {Directory}.", directory);
            TryDeleteDirectory(directory);
            return [];
        }

        if (staged.Count == 0)
        {
            TryDeleteDirectory(directory);
        }

        return staged;
    }

    private void CleanupVisionStaging(IReadOnlyList<StagedVisionImage> staged)
    {
        foreach (var directory in staged.Select(s => s.Directory).Distinct(StringComparer.Ordinal))
        {
            TryDeleteDirectory(directory);
        }
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Claude -p vision: could not delete staging directory {Directory}.", directory);
        }
    }

    private static string ExtensionFor(string? mediaType) =>
        mediaType?.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".png",
        };

    /// <summary>
    /// Prefixes the caller's prompt with the staged image manifest. Paths are listed one per line as
    /// <c>- &lt;absolute path&gt;</c> so the model can open each with the Read tool before answering.
    /// </summary>
    internal static string BuildStandardInput(string input, IReadOnlyList<StagedVisionImage> staged)
    {
        if (staged.Count == 0)
        {
            return input;
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine(
            $"The visual input for this request is {staged.Count} image file(s) on the local filesystem. " +
            "Use the Read tool to open EVERY one of them before you answer. Base your answer on what you " +
            "actually observe in these pixels — never on assumptions about the subject matter.");
        builder.AppendLine();

        foreach (var image in staged)
        {
            builder.AppendLine($"- {image.Path}");
        }

        builder.AppendLine();
        builder.Append(input);
        return builder.ToString();
    }

    /// <summary>A reference image written to disk for the lifetime of a single CLI invocation.</summary>
    internal sealed record StagedVisionImage(string Path, string Directory);

    internal static IReadOnlyList<string> BuildArguments(
        AiCompletionRequest request,
        ClaudePromptCliSettings settings,
        string model,
        bool requiresImageRead = false)
    {
        // Print mode. The prompt itself is NOT passed here — it is delivered via stdin by the caller
        // (ProcessRunRequest.StandardInput) so large prompts don't exceed the Windows command-line
        // limit (Win32 206 "filename or extension too long"). See issue #69.
        var args = new List<string> { "-p" };
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

        var allowedTools = settings.AllowedTools.ToList();

        // Staged vision images are useless unless the CLI is allowed to open them.
        if (requiresImageRead && !allowedTools.Contains("Read", StringComparer.Ordinal))
        {
            allowedTools.Add("Read");
        }

        foreach (var tool in allowedTools)
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
        args.Add(BuildIsolatedSystemPrompt(request.OutputJsonSchema, requiresImageRead));
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

    /// <summary>
    /// The isolation prompt otherwise tells the model there is no task and no tooling, which would
    /// discourage it from opening the staged files. This clause carves out the one tool it needs.
    /// </summary>
    private const string VisionClause =
        " This request DOES include visual input: image files listed by absolute path in the payload. " +
        "Use the Read tool to open every listed path before answering. That is the only tool you may use.";

    private static string BuildIsolatedSystemPrompt(string? outputJsonSchema, bool requiresImageRead)
    {
        var prompt = requiresImageRead
            ? IsolatedRendererSystemPrompt + VisionClause
            : IsolatedRendererSystemPrompt;

        return string.IsNullOrWhiteSpace(outputJsonSchema)
            ? prompt
            : $"{prompt} Required JSON schema: {outputJsonSchema}";
    }

    private static string Preview(string text) => text.Length <= 400 ? text : text[..400] + "…";
}
