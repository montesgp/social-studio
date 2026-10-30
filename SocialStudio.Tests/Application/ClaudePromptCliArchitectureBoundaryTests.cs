namespace SocialStudio.Tests.Application;

public sealed class ClaudePromptCliArchitectureBoundaryTests
{
    [Theory]
    [InlineData("SocialStudio.Application")]
    [InlineData("SocialStudio.Domain")]
    public void ProcessAndClaudePromptCliTypes_DoNotLeakIntoApplicationOrDomain(string projectDirectory)
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, projectDirectory), "*.cs", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("System.Diagnostics.Process", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ClaudePromptCli", text, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SocialStudio.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
