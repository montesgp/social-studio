namespace SocialStudio.Tests.Architecture;

/// <summary>
/// WU-6 (N3, design §3 guard 1): <c>Metric.From</c> has a private constructor and exactly ONE calling
/// type across the Application assembly — <c>DeterministicMetricCalculator</c>. A source-text scan,
/// same idiom as <see cref="NoActorLiteralGuardTests"/>/the country-name guard: no IL/Cecil dependency
/// needed for a call-site-count assertion this simple.
///
/// Written BEFORE <c>DeterministicMetricCalculator</c> exists (task 6.1), so it is vacuously RED (zero
/// calling files found, not the expected one) until task 6.3 lands the calculator — re-run at 6.4 to
/// confirm it turns MEANINGFULLY green (the real calling type is found), not just "no callers found".
/// </summary>
public sealed class NicheDiscoveryMetricFactoryGuardTests
{
    [Fact]
    public void MetricFrom_HasExactlyOneCallingFile_DeterministicMetricCalculator()
    {
        var root = FindRepositoryRoot();
        var applicationDir = Path.Combine(root, "SocialStudio.Application");

        var callingFiles = Directory.EnumerateFiles(applicationDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), "Metric.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("Metric.From(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["DeterministicMetricCalculator.cs"], callingFiles);
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
