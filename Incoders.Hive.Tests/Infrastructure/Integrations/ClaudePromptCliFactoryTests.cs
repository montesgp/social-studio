using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Process;
using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Tests.Infrastructure.Integrations;

public sealed class ClaudePromptCliFactoryTests
{
    [Fact]
    public void Create_ReturnsTextClientForClaudePromptProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner, NoopProcessRunner>();
        var factory = new ExternalProviderClientFactory(services.BuildServiceProvider());

        var client = factory.Create<IAiModelClient>(BuildCredential());

        Assert.IsType<ClaudePromptCliModelClient>(client);
    }

    [Fact]
    public void Create_ReturnsImageClientForClaudePromptProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner, NoopProcessRunner>();
        var factory = new ExternalProviderClientFactory(services.BuildServiceProvider());

        var client = factory.Create<IImageModelClient>(BuildCredential());

        Assert.IsType<ClaudePromptCliImageModelClient>(client);
    }

    private static ResolvedProviderCredential BuildCredential() =>
        new(Guid.Empty, "claude-p", IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["executable_path"] = "claude" }, "claude-sonnet-4-6");

    private sealed class NoopProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessRunResult(0, "{}", "", false));
    }
}
