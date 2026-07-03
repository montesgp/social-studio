using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;
using Incoders.Hive.Infrastructure.Process;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

public sealed class ClaudePromptCliModelClientTests
{
    [Fact]
    public async Task CompleteAsync_InvokesWhitelistedClaudePromptCommandWithSelectedModelAndSchema()
    {
        var runner = new CapturingProcessRunner(new ProcessRunResult(0, "{\"ok\":true}", "", false));
        var client = new ClaudePromptCliModelClient(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = "claude",
            ["default_model"] = "claude-default",
            ["timeout_ms"] = "30000",
            ["output_format"] = "json"
        }), runner, NullLogger<ClaudePromptCliModelClient>.Instance);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            Guid.NewGuid(), "ai-analysis", "prompt/v1", "hello", "claude-p", "claude-selected", "{\"type\":\"object\"}"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("{\"ok\":true}", result.Value.Output);
        Assert.Equal("claude-p", result.Value.ProviderKey);
        Assert.Equal("claude-selected", result.Value.Model);
        Assert.Equal("claude", runner.Request!.FileName);
        Assert.Equal("-p", runner.Request.Arguments[0]);
        Assert.Equal("hello", runner.Request.Arguments[1]);
        Assert.Contains("--model", runner.Request.Arguments);
        Assert.Contains("claude-selected", runner.Request.Arguments);
        Assert.Contains("--output-format", runner.Request.Arguments);
        Assert.Contains("json", runner.Request.Arguments);
    }

    [Fact]
    public async Task CompleteAsync_IsolatesClaudeFromEngineeringEcosystem_WithSystemPromptOverrideAndSchema()
    {
        var runner = new CapturingProcessRunner(new ProcessRunResult(0, "{\"ok\":true}", "", false));
        var client = new ClaudePromptCliModelClient(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = "claude",
            ["default_model"] = "claude-default"
        }), runner, NullLogger<ClaudePromptCliModelClient>.Instance);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            Guid.NewGuid(), "ai-analysis", "prompt/v1", "hello", "claude-p", "claude-selected", "{\"type\":\"object\"}"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var args = runner.Request!.Arguments;

        // The default agent system prompt is REPLACED (override), not appended — this is what strips
        // the Claude Code identity and the dynamic cwd/git sections that caused the refusals.
        Assert.Contains("--system-prompt", args);
        Assert.DoesNotContain("--append-system-prompt", args);

        var systemPrompt = args[args.ToList().IndexOf("--system-prompt") + 1];
        Assert.Contains("non-interactive rendering function", systemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never refuse", systemPrompt, StringComparison.OrdinalIgnoreCase);
        // Schema is folded into the override.
        Assert.Contains("{\"type\":\"object\"}", systemPrompt);

        // Skills and MCP (engram + any harness tooling) are disabled for this atomic invocation.
        Assert.Contains("--disable-slash-commands", args);
        Assert.Contains("--strict-mcp-config", args);
    }

    [Fact]
    public async Task CompleteAsync_AppliesIsolationEvenWithoutSchema()
    {
        var runner = new CapturingProcessRunner(new ProcessRunResult(0, "{}", "", false));
        var client = new ClaudePromptCliModelClient(BuildCredential(), runner, NullLogger<ClaudePromptCliModelClient>.Instance);

        var result = await client.CompleteAsync(new AiCompletionRequest(
            Guid.NewGuid(), "ai-analysis", "prompt/v1", "hello", "claude-p", "claude-selected"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var args = runner.Request!.Arguments;
        Assert.Contains("--system-prompt", args);
        Assert.DoesNotContain("--append-system-prompt", args);
        Assert.Contains("--disable-slash-commands", args);
        Assert.Contains("--strict-mcp-config", args);
    }

    [Fact]
    public async Task CompleteAsync_NonZeroExitReturnsProviderFailure()
    {
        var runner = new CapturingProcessRunner(new ProcessRunResult(2, "", "boom", false));
        var client = new ClaudePromptCliModelClient(BuildCredential(), runner, NullLogger<ClaudePromptCliModelClient>.Instance);

        var result = await client.CompleteAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ExternalProviderUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task CompleteAsync_TimeoutReturnsProviderTimeout()
    {
        var runner = new CapturingProcessRunner(new ProcessRunResult(-1, "", "", true));
        var client = new ClaudePromptCliModelClient(BuildCredential(), runner, NullLogger<ClaudePromptCliModelClient>.Instance);

        var result = await client.CompleteAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderTimeout.Code, result.Error.Code);
    }

    private static AiCompletionRequest BuildRequest() =>
        new(Guid.NewGuid(), "ai-analysis", "prompt/v1", "hello", "claude-p", "claude-sonnet-4-6");

    private static ResolvedProviderCredential BuildCredential(IReadOnlyDictionary<string, string>? values = null) =>
        new(Guid.Empty, "claude-p", IntegrationConnectionMode.BringYourOwnKey,
            values ?? new Dictionary<string, string> { ["executable_path"] = "claude" }, "claude-default");
}

file sealed class CapturingProcessRunner : IProcessRunner
{
    private readonly ProcessRunResult _result;
    public ProcessRunRequest? Request { get; private set; }
    public CapturingProcessRunner(ProcessRunResult result) => _result = result;
    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        Request = request;
        return Task.FromResult(_result);
    }
}
