using System.Text.RegularExpressions;

namespace Incoders.Hive.Tests.Architecture;

/// <summary>
/// ND-15: no identifier or string literal produced by this change may be named after a specific
/// country. Scans the four NicheDiscovery namespaces plus the web niche-discovery folders for
/// country names/demonyms and the bare "AR" country-code fragment, whitelisting only
/// <c>MarketCatalogSeeder.cs</c> (seed DATA legitimately names a market) and locale/i18n resource
/// files (design §4, §16 Guard row).
///
/// Written RED first (task 2.7): <see cref="SeededViolationFixture_IsDetectedByTheGuard"/> proves the
/// scan mechanism actually fires on a deliberately seeded violation, not merely "nothing exists yet
/// so it trivially passes". <see cref="RealShippedTree_ContainsNoCountryNamedIdentifierOrLiteral"/> is
/// re-run as a cheap regression check at the end of every subsequent work unit.
/// </summary>
public sealed class NicheDiscoveryCountryNameGuardTests
{
    private static readonly string[] GuardedRelativeDirectories =
    [
        "Incoders.Hive.Domain/Social/NicheDiscovery",
        "Incoders.Hive.Application/Abstractions/Social/NicheDiscovery",
        "Incoders.Hive.Application/Features/Social/NicheDiscovery",
        "Incoders.Hive.Infrastructure/Social/NicheDiscovery",
        "apps/web/lib/social/niche-discovery",
        "apps/web/components/social/niche-discovery",
    ];

    private static readonly string[] WhitelistedFileNames =
    [
        "MarketCatalogSeeder.cs",
    ];

    // Demonyms plus a representative set of ISO-3166 country names. Whole-word matched,
    // case-insensitive, comments stripped first. Extend this list as new markets are seeded.
    private static readonly string[] CountryNamesAndDemonyms =
    [
        "Argentina", "Argentine", "Argentinian",
        "Brazil", "Brazilian",
        "Chile", "Chilean",
        "Colombia", "Colombian",
        "Mexico", "Mexican",
        "Peru", "Peruvian",
        "Uruguay", "Uruguayan",
        "Paraguay", "Paraguayan",
        "Bolivia", "Bolivian",
        "Ecuador", "Ecuadorian",
        "Venezuela", "Venezuelan",
        "Spain", "Spanish",
        "Portugal", "Portuguese",
        "France", "French",
        "Germany", "German",
        "Italy", "Italian",
        "Canada", "Canadian",
    ];

    // The seed market (Argentina) reduces to the bare ISO-3166-1 alpha-2 code "AR" — guarded as a
    // standalone token so "ArgentinaCompetitionScore"-style violations are caught even when someone
    // tries to shorten the identifier to the code instead of the demonym.
    private static readonly Regex BareArCodePattern = new(@"(?<![A-Za-z])AR(?![A-Za-z])", RegexOptions.Compiled);

    private static readonly Regex LineCommentPattern = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void RealShippedTree_ContainsNoCountryNamedIdentifierOrLiteral()
    {
        var offenders = ScanTree(FindRepositoryRoot());

        Assert.True(
            offenders.Count == 0,
            "No identifier or string literal may be named after a country (ND-15). Offenders:\n"
                + string.Join('\n', offenders));
    }

    [Fact]
    public void SeededViolationFixture_IsDetectedByTheGuard()
    {
        var tempRoot = Directory.CreateTempSubdirectory("niche-discovery-guard-fixture");
        try
        {
            var guardedDir = Path.Combine(tempRoot.FullName, "Incoders.Hive.Domain", "Social", "NicheDiscovery");
            Directory.CreateDirectory(guardedDir);
            File.WriteAllText(
                Path.Combine(guardedDir, "ArgentinaCompetitionScore.cs"),
                "namespace Fixture; public sealed class ArgentinaCompetitionScore { }");

            var offenders = ScanTree(tempRoot.FullName);

            Assert.NotEmpty(offenders);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void SeededBareCodeFragmentFixture_IsDetectedByTheGuard()
    {
        var tempRoot = Directory.CreateTempSubdirectory("niche-discovery-guard-fixture-code");
        try
        {
            var guardedDir = Path.Combine(tempRoot.FullName, "Incoders.Hive.Domain", "Social", "NicheDiscovery");
            Directory.CreateDirectory(guardedDir);
            File.WriteAllText(
                Path.Combine(guardedDir, "Sample.cs"),
                "namespace Fixture; public sealed class Sample { public const string Code = \"AR\"; }");

            var offenders = ScanTree(tempRoot.FullName);

            Assert.NotEmpty(offenders);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void WhitelistedSeederFile_IsNeverScanned()
    {
        var tempRoot = Directory.CreateTempSubdirectory("niche-discovery-guard-fixture-whitelist");
        try
        {
            var guardedDir = Path.Combine(tempRoot.FullName, "Incoders.Hive.Infrastructure", "Social", "NicheDiscovery");
            Directory.CreateDirectory(guardedDir);
            File.WriteAllText(
                Path.Combine(guardedDir, "MarketCatalogSeeder.cs"),
                "namespace Fixture; public sealed class MarketCatalogSeeder { public const string Seed = \"Argentina\"; }");

            var offenders = ScanTree(tempRoot.FullName);

            Assert.Empty(offenders);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    private static List<string> ScanTree(string root)
    {
        var offenders = new List<string>();

        foreach (var relativeDir in GuardedRelativeDirectories)
        {
            var fullDir = Path.Combine(root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullDir))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(fullDir, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath);
                var normalizedPath = filePath.Replace('\\', '/');

                if (WhitelistedFileNames.Contains(fileName, StringComparer.Ordinal))
                {
                    continue;
                }

                // i18n/locale resource files legitimately spell out a market's display name.
                if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                    (normalizedPath.Contains("/locales/", StringComparison.OrdinalIgnoreCase) ||
                     normalizedPath.Contains("/i18n/", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawText = File.ReadAllText(filePath);
                var text = LineCommentPattern.Replace(rawText, string.Empty);

                foreach (var name in CountryNamesAndDemonyms)
                {
                    // Deliberately a SUBSTRING match, not whole-word: a country name can be embedded
                    // in a PascalCase identifier with no separator (e.g. "ArgentinaCompetitionScore",
                    // "IsViableInArgentina") exactly as ND-15's forbidden examples show.
                    if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{filePath}: country-named token '{name}'");
                    }
                }

                if (BareArCodePattern.IsMatch(text))
                {
                    offenders.Add($"{filePath}: bare 'AR' country-code fragment");
                }
            }
        }

        return offenders;
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
