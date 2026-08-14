namespace Incoders.Hive.Tests.Architecture;

/// <summary>
/// WU-8 (guard c, design §3): no method on any type in <c>…NicheDiscovery.Scoring</c> or
/// <c>…NicheDiscovery.Analytics</c> has a parameter or return type declared in
/// <c>…NicheDiscovery.Semantics</c>. Source-text scan, same idiom as
/// <see cref="NicheDiscoveryMetricFactoryGuardTests"/>/the country-name guard.
///
/// This is meaningful NOW even though the <c>Semantics</c> namespace does not exist yet: an
/// empty/nonexistent namespace trivially satisfies "no reference to it". It MUST be re-run and
/// re-verified once WU-10 lands the real <c>Semantics</c> types, WITHOUT modification — that re-run is
/// task 10.6, extending this same file rather than duplicating it.
/// </summary>
public sealed class NicheDiscoveryScoringSemanticsGuardTests
{
    [Fact]
    public void ScoringAndAnalyticsFiles_NeverReferenceTheSemanticsNamespace()
    {
        var root = FindRepositoryRoot();
        var applicationDir = Path.Combine(root, "Incoders.Hive.Application");

        var scoringDir = Path.Combine(applicationDir, "Features", "Social", "NicheDiscovery", "Scoring");
        var analyticsDir = Path.Combine(applicationDir, "Features", "Social", "NicheDiscovery", "Analytics");

        var candidateFiles = Enumerable.Empty<string>();

        if (Directory.Exists(scoringDir))
        {
            candidateFiles = candidateFiles.Concat(Directory.EnumerateFiles(scoringDir, "*.cs", SearchOption.AllDirectories));
        }

        if (Directory.Exists(analyticsDir))
        {
            candidateFiles = candidateFiles.Concat(Directory.EnumerateFiles(analyticsDir, "*.cs", SearchOption.AllDirectories));
        }

        var violations = candidateFiles
            .Where(path => File.ReadAllText(path).Contains("NicheDiscovery.Semantics", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(violations);
    }

    /// <summary>
    /// Task 10.6 re-verification: the scan above is now MEANINGFUL, not vacuous — the
    /// <c>NicheDiscovery.Semantics</c> namespace this test scans for genuinely exists (WU-10 landed
    /// <c>SemanticAnnotation</c>, <c>ClusterProposal</c>, <c>FormatTrait</c>, <c>SemanticAnnotator</c>,
    /// <c>LexicalClusterFallback</c>), and the assertion above still holds now that there is something
    /// to violate it with.
    /// </summary>
    [Fact]
    public void TheSemanticsNamespaceThisGuardScansFor_NowGenuinelyExists()
    {
        var root = FindRepositoryRoot();
        var semanticsDir = Path.Combine(
            root, "Incoders.Hive.Application", "Features", "Social", "NicheDiscovery", "Semantics");

        Assert.True(Directory.Exists(semanticsDir));
        Assert.NotEmpty(Directory.EnumerateFiles(semanticsDir, "*.cs", SearchOption.AllDirectories));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Incoders.Hive.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
