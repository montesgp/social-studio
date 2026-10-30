using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;

namespace SocialStudio.Tests.Infrastructure.Ai;

public sealed class ClaudePromptCliSettingsTests
{
    [Fact]
    public void FromCredential_AcceptsSafeExecutableModelTimeoutAndOutputPolicy()
    {
        var outputDir = Directory.CreateTempSubdirectory("claude-p-output");
        var workingDir = Directory.CreateTempSubdirectory("claude-p-work");
        var settings = ClaudePromptCliSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = "claude",
            ["working_directory"] = workingDir.FullName,
            ["allowed_output_directory"] = outputDir.FullName,
            ["default_model"] = "claude-sonnet-4-6",
            ["timeout_ms"] = "45000",
            ["output_format"] = "json",
            ["permission_mode"] = "acceptEdits",
            ["allowed_tools"] = "Read,Write"
        }));

        Assert.True(settings.IsSuccess);
        Assert.Equal("claude", settings.Value.ExecutablePath);
        Assert.Equal("claude-sonnet-4-6", settings.Value.DefaultModel);
        Assert.Equal(TimeSpan.FromMilliseconds(45000), settings.Value.Timeout);
        Assert.Equal("json", settings.Value.OutputFormat);
        Assert.Equal(["Read", "Write"], settings.Value.AllowedTools);
    }

    [Theory]
    [InlineData("claude && rm -rf /")]
    [InlineData("claude; calc")]
    [InlineData("cmd /c claude")]
    [InlineData("powershell -Command claude")]
    public void FromCredential_RejectsUnsafeExecutableValues(string executable)
    {
        var result = ClaudePromptCliSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = executable
        }));

        Assert.True(result.IsFailure);
    }


    [Theory]
    [InlineData("python")]
    [InlineData("node")]
    [InlineData(@"C:\Tools\not-claude.exe")]
    public void FromCredential_RejectsExecutableNamesThatAreNotClaudeCli(string executable)
    {
        var result = ClaudePromptCliSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = executable
        }));

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("claude.exe")]
    [InlineData("claude.cmd")]
    [InlineData(@"C:\Tools\claude.exe")]
    [InlineData("/usr/local/bin/claude")]
    [InlineData(@"/opt/tools\claude.cmd")]
    [InlineData(@"C:\Tools/claude.exe")]
    public void FromCredential_AcceptsOnlyClaudeCliBasenames(string executable)
    {
        var result = ClaudePromptCliSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = executable
        }));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("claude.exe.bak")]
    [InlineData("notclaude")]
    [InlineData(@"C:\Tools\claude.exe\")]
    [InlineData("/usr/local/bin/claude/")]
    [InlineData(@"C:\Tools\not-claude.exe")]
    public void FromCredential_RejectsLookalikesAndPathsWithoutAllowedBasename(string executable)
    {
        var result = ClaudePromptCliSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["executable_path"] = executable
        }));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void FromCredential_RejectsInvalidTimeout()
    {
        var result = ClaudePromptCliSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["timeout_ms"] = "0"
        }));

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.RoutingCandidateTimeoutInvalid.Code, result.Error.Code);
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("markdown")]
    public void FromCredential_RejectsInvalidOutputFormat(string outputFormat)
    {
        var result = ClaudePromptCliSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["output_format"] = outputFormat
        }));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void FromCredential_RejectsNonExistingConfiguredDirectories()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
        var result = ClaudePromptCliSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["working_directory"] = missing
        }));

        Assert.True(result.IsFailure);
    }

    private static ResolvedProviderCredential BuildCredential(IReadOnlyDictionary<string, string> values) =>
        new(Guid.Empty, "claude-p", IntegrationConnectionMode.BringYourOwnKey, values, SelectedModel: null);
}
