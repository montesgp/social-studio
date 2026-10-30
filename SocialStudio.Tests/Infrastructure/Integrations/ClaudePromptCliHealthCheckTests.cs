using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Infrastructure.Integrations.HealthChecks;
using SocialStudio.Infrastructure.Process;

namespace SocialStudio.Tests.Infrastructure.Integrations;

public sealed class ClaudePromptCliHealthCheckTests
{
    [Fact]
    public async Task ProviderKey_IsClaudeP()
    {
        var check = new ClaudePromptCliIntegrationHealthCheck(new StubProcessRunner(new ProcessRunResult(0, "1.0.0", "", false)));

        Assert.Equal("claude-p", check.ProviderKey);
    }

    [Fact]
    public async Task Success_RunsClaudeVersionAndExitZero()
    {
        var runner = new StubProcessRunner(new ProcessRunResult(0, "1.2.3 (Claude Code)", "", false));
        var check = new ClaudePromptCliIntegrationHealthCheck(runner);

        var result = await check.CheckAsync(new Dictionary<string, string>(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("claude-p", result.Provider);
        Assert.Equal("claude", runner.Request!.FileName);
        Assert.Equal(new[] { "--version" }, runner.Request.Arguments);
    }

    [Fact]
    public async Task HonorsConfiguredExecutablePath()
    {
        var runner = new StubProcessRunner(new ProcessRunResult(0, "ok", "", false));
        var check = new ClaudePromptCliIntegrationHealthCheck(runner);

        await check.CheckAsync(new Dictionary<string, string> { ["executable_path"] = "claude.cmd" }, CancellationToken.None);

        Assert.Equal("claude.cmd", runner.Request!.FileName);
    }

    [Fact]
    public async Task NonZeroExit_ReturnsFailure()
    {
        var check = new ClaudePromptCliIntegrationHealthCheck(new StubProcessRunner(new ProcessRunResult(127, "", "not found", false)));

        var result = await check.CheckAsync(new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("nonzero_exit", result.ErrorCode);
        Assert.Equal(127, result.StatusCode);
    }

    [Fact]
    public async Task Timeout_ReturnsFailure()
    {
        var check = new ClaudePromptCliIntegrationHealthCheck(new StubProcessRunner(new ProcessRunResult(-1, "", "", true)));

        var result = await check.CheckAsync(new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("timeout", result.ErrorCode);
    }

    [Fact]
    public async Task LaunchFailure_ReturnsCliUnavailable()
    {
        var check = new ClaudePromptCliIntegrationHealthCheck(new ThrowingProcessRunner());

        var result = await check.CheckAsync(new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("cli_unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task InvalidExecutable_ReturnsFailureWithoutLaunching()
    {
        var runner = new StubProcessRunner(new ProcessRunResult(0, "", "", false));
        var check = new ClaudePromptCliIntegrationHealthCheck(runner);

        var result = await check.CheckAsync(new Dictionary<string, string> { ["executable_path"] = "rm -rf" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(runner.Request);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly ProcessRunResult _result;

        public StubProcessRunner(ProcessRunResult result) => _result = result;

        public ProcessRunRequest? Request { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) =>
            throw new System.ComponentModel.Win32Exception("The system cannot find the file specified.");
    }
}
