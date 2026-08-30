using System.ComponentModel;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;
using Incoders.Hive.Infrastructure.Process;

namespace Incoders.Hive.Infrastructure.Integrations.HealthChecks;

/// <summary>
/// Real Claude -p connectivity check. claude-p is a backend-local CLI provider, so "connected" means the
/// configured executable launches on the host and `claude --version` exits 0 — not an HTTP probe. Host
/// tuning (executable path, working directory) is validated through <see cref="ClaudePromptCliSettings"/>,
/// reusing the same shell-injection guards as the runtime client.
/// </summary>
internal sealed class ClaudePromptCliIntegrationHealthCheck : IExternalIntegrationHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _processRunner;

    public ClaudePromptCliIntegrationHealthCheck(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string ProviderKey => ClaudePromptCliSettings.ProviderKey;

    public async Task<IntegrationHealthResult> CheckAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            ProviderKey,
            IntegrationConnectionMode.ManagedByIncoders,
            credentials,
            SelectedModel: null);

        var settings = ClaudePromptCliSettings.FromCredential(credential);
        if (settings.IsFailure)
        {
            return new IntegrationHealthResult(false, ProviderKey, null, settings.Error.Code, "Invalid Claude -p host configuration.");
        }

        ProcessRunResult result;
        try
        {
            result = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    settings.Value.ExecutablePath,
                    ["--version"],
                    settings.Value.WorkingDirectory,
                    ProbeTimeout),
                cancellationToken);
        }
        catch (Win32Exception ex)
        {
            return new IntegrationHealthResult(false, ProviderKey, null, "cli_unavailable", $"Could not launch Claude CLI '{settings.Value.ExecutablePath}': {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new IntegrationHealthResult(false, ProviderKey, null, "cli_unavailable", $"Could not launch Claude CLI '{settings.Value.ExecutablePath}': {ex.Message}");
        }

        if (result.TimedOut)
        {
            return new IntegrationHealthResult(false, ProviderKey, null, "timeout", "Claude CLI '--version' timed out.");
        }

        if (result.ExitCode != 0)
        {
            return new IntegrationHealthResult(false, ProviderKey, result.ExitCode, "nonzero_exit", $"Claude CLI exited with code {result.ExitCode}.");
        }

        return new IntegrationHealthResult(true, ProviderKey, result.ExitCode, null, result.StandardOutput.Trim());
    }
}
