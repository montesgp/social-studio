using System.Text.RegularExpressions;

namespace Incoders.Hive.Tests.Architecture;

/// <summary>
/// WU-11b (ND-17, design §12): NicheDiscovery references no <c>ContentGeneration</c> type, and
/// <c>ContentGeneration</c> references no NicheDiscovery type. Source-text scan, same idiom as
/// <see cref="NicheDiscoveryScoringSemanticsGuardTests"/> — the Content Generator activation seam
/// (<c>INicheOpportunityActivationSource</c>) is named, not built (§12), so neither capability may lean
/// on the other's internals.
/// </summary>
public sealed class NicheDiscoveryContentGenerationIsolationGuardTests
{
    // Doc comments legitimately name the sibling capability for comparison ("mirrors
    // Social.ContentGeneration.Discovery.OpportunityScorer") — that convention is established and
    // encouraged elsewhere in this change (WU-2/9/10's own class docs). Only a live code reference
    // (using directive, fully-qualified type usage) is a real violation, so comments are stripped
    // first, same as the country-name guard (WU-2.7).
    private static readonly Regex LineCommentPattern = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void NicheDiscoveryFiles_NeverReferenceTheContentGenerationNamespace()
    {
        var root = FindRepositoryRoot();

        var nicheDiscoveryDirs = new[]
        {
            Path.Combine(root, "Incoders.Hive.Domain", "Social", "NicheDiscovery"),
            Path.Combine(root, "Incoders.Hive.Application", "Abstractions", "Social", "NicheDiscovery"),
            Path.Combine(root, "Incoders.Hive.Application", "Features", "Social", "NicheDiscovery"),
            Path.Combine(root, "Incoders.Hive.Infrastructure", "Social", "NicheDiscovery"),
        };

        var violations = ScanFor(nicheDiscoveryDirs, "ContentGeneration");

        Assert.True(violations.Count == 0, "No NicheDiscovery type may reference ContentGeneration (ND-17). Offenders:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void ContentGenerationFiles_NeverReferenceTheNicheDiscoveryNamespace()
    {
        var root = FindRepositoryRoot();

        var contentGenerationDirs = new[]
        {
            Path.Combine(root, "Incoders.Hive.Domain", "Social", "ContentGeneration"),
            Path.Combine(root, "Incoders.Hive.Application", "Abstractions", "Social", "ContentGeneration"),
            Path.Combine(root, "Incoders.Hive.Application", "Features", "Social", "ContentGeneration"),
            Path.Combine(root, "Incoders.Hive.Infrastructure", "Social", "ContentGeneration"),
        };

        var violations = ScanFor(contentGenerationDirs, "NicheDiscovery");

        Assert.True(violations.Count == 0, "No ContentGeneration type may reference NicheDiscovery (ND-17). Offenders:\n" + string.Join('\n', violations));
    }

    private static List<string> ScanFor(IReadOnlyList<string> directories, string forbiddenNamespaceFragment)
    {
        var candidateFiles = Enumerable.Empty<string>();

        foreach (var dir in directories)
        {
            if (Directory.Exists(dir))
            {
                candidateFiles = candidateFiles.Concat(Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories));
            }
        }

        return candidateFiles
            .Where(path => LineCommentPattern.Replace(File.ReadAllText(path), string.Empty)
                .Contains(forbiddenNamespaceFragment, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList()!;
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
