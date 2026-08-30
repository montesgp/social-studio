using System.Globalization;
using System.Text;
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

    /// <summary>
    /// Every ISO-3166 region name .NET knows about, normalized to a comparable form. DERIVED, not
    /// curated: a hand-maintained list stops protecting the moment someone seeds a market it never
    /// heard of, and — worse — cannot tell you that it has stopped. Deriving from
    /// <see cref="RegionInfo"/> means coverage can never drift behind the market catalog.
    /// </summary>
    private static readonly HashSet<string> CountryNames = BuildCountryNames();

    /// <summary>
    /// Demonyms have no equivalent in <see cref="RegionInfo"/>, so this half stays curated. It is
    /// supplementary: a demonym-named identifier (<c>ArgentinianCompetitionScore</c>) is caught here,
    /// while the country name itself is caught by the derived set above regardless of market.
    /// </summary>
    private static readonly HashSet<string> Demonyms = new(StringComparer.Ordinal)
    {
        "argentine", "argentinian", "brazilian", "chilean", "colombian", "mexican", "peruvian",
        "uruguayan", "paraguayan", "bolivian", "ecuadorian", "venezuelan", "spanish", "portuguese",
        "french", "german", "italian", "canadian", "indonesian", "japanese", "nigerian",
        "vietnamese", "polish", "filipino", "american", "british", "indian",
    };

    // The seed market (Argentina) reduces to the bare ISO-3166-1 alpha-2 code "AR" — guarded as a
    // standalone token so "ArgentinaCompetitionScore"-style violations are caught even when someone
    // tries to shorten the identifier to the code instead of the demonym.
    private static readonly Regex BareArCodePattern = new(@"(?<![A-Za-z])AR(?![A-Za-z])", RegexOptions.Compiled);

    private static readonly Regex LineCommentPattern = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>An identifier word: a capitalized or lowercase run, so PascalCase splits correctly.</summary>
    private static readonly Regex WordPattern = new(@"\p{Lu}+(?![\p{Ll}])|\p{Lu}[\p{Ll}]+|[\p{Ll}]+", RegexOptions.Compiled);

    /// <summary>Longest ISO region name in words ("United States Minor Outlying Islands" and friends).</summary>
    private const int MaxCountryNameWords = 4;

    /// <summary>
    /// Guards against a degenerate short region name matching an ordinary identifier word outright.
    /// Nothing in ISO-3166 is shorter than this once normalized.
    /// </summary>
    private const int MinCountryNameLength = 4;

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

    /// <summary>
    /// The guard is only as good as its coverage. A hand-curated list silently stops protecting the
    /// moment someone seeds a market it never heard of — which is exactly the failure mode ND-15
    /// exists to prevent, and the one a curated list cannot warn you about. These cases cover
    /// countries outside the originally curated Latin America / Western Europe set.
    /// </summary>
    [Theory]
    [InlineData("Indonesia")]
    [InlineData("Japan")]
    [InlineData("Nigeria")]
    [InlineData("Vietnam")]
    [InlineData("Poland")]
    [InlineData("Philippines")]
    public void SeededViolationOutsideTheOriginallyCuratedRegions_IsDetectedByTheGuard(string country)
    {
        var tempRoot = Directory.CreateTempSubdirectory("niche-discovery-guard-fixture-coverage");
        try
        {
            var guardedDir = Path.Combine(tempRoot.FullName, "Incoders.Hive.Domain", "Social", "NicheDiscovery");
            Directory.CreateDirectory(guardedDir);
            File.WriteAllText(
                Path.Combine(guardedDir, "Sample.cs"),
                $"namespace Fixture; public sealed class {country}CompetitionScore {{ }}");

            var offenders = ScanTree(tempRoot.FullName);

            Assert.NotEmpty(offenders);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Multi-word country names are the case a naive scanner misses: the words are legitimate on
    /// their own ("United", "South", "Costa"), so only the joined identifier is a violation.
    /// </summary>
    [Theory]
    [InlineData("SouthKorea")]
    [InlineData("CostaRica")]
    [InlineData("UnitedStates")]
    public void SeededMultiWordCountryViolation_IsDetectedByTheGuard(string country)
    {
        var tempRoot = Directory.CreateTempSubdirectory("niche-discovery-guard-fixture-multiword");
        try
        {
            var guardedDir = Path.Combine(tempRoot.FullName, "Incoders.Hive.Domain", "Social", "NicheDiscovery");
            Directory.CreateDirectory(guardedDir);
            File.WriteAllText(
                Path.Combine(guardedDir, "Sample.cs"),
                $"namespace Fixture; public sealed class IsViableIn{country} {{ }}");

            var offenders = ScanTree(tempRoot.FullName);

            Assert.NotEmpty(offenders);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Coverage is worthless if it is bought with false positives: a guard that cries wolf gets
    /// suppressed, and then it protects nothing. These are ordinary words that CONTAIN a country
    /// name as a substring ("malicious" contains "Mali", "Romanian" contains "Oman", "woman"
    /// contains "Oman") and must not be reported.
    /// </summary>
    [Theory]
    [InlineData("public sealed class MaliciousInputGuard { }")]
    [InlineData("public sealed class RomanNumeralFormatter { }")]
    [InlineData("public sealed class WomanAudienceSegment { }")]
    [InlineData("public sealed class ChildSafetyFilter { }")]
    [InlineData("public sealed class GrandTotalAccumulator { }")]
    public void OrdinaryWordsContainingACountryNameAsSubstring_AreNotReported(string source)
    {
        var tempRoot = Directory.CreateTempSubdirectory("niche-discovery-guard-fixture-falsepositive");
        try
        {
            var guardedDir = Path.Combine(tempRoot.FullName, "Incoders.Hive.Domain", "Social", "NicheDiscovery");
            Directory.CreateDirectory(guardedDir);
            File.WriteAllText(Path.Combine(guardedDir, "Sample.cs"), $"namespace Fixture; {source}");

            var offenders = ScanTree(tempRoot.FullName);

            Assert.Empty(offenders);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void GuardedCountryVocabulary_CoversEveryIsoRegion_NotACuratedSubset()
    {
        // The point of deriving from RegionInfo rather than hand-maintaining a list: coverage cannot
        // drift behind the markets someone seeds later.
        Assert.True(
            CountryNames.Count > 200,
            $"Expected ISO-derived country coverage, got only {CountryNames.Count} names — the guard "
                + "has silently fallen back to a curated subset.");
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

                foreach (var hit in FindCountryTokens(text))
                {
                    offenders.Add($"{filePath}: country-named token '{hit}'");
                }

                if (BareArCodePattern.IsMatch(text))
                {
                    offenders.Add($"{filePath}: bare 'AR' country-code fragment");
                }
            }
        }

        return offenders;
    }

    /// <summary>
    /// Splits source into identifier words — on case boundaries, underscores, dots and punctuation —
    /// then matches whole words rather than substrings.
    ///
    /// Whole-word matching is the point. A substring scan over the complete ISO set reports "Mali"
    /// inside <c>malicious</c>, "Oman" inside <c>Roman</c> and <c>woman</c>, and "Chad" inside
    /// <c>chadless</c>; a guard that cries wolf gets suppressed, and a suppressed guard protects
    /// nothing. Splitting on case boundaries keeps the PascalCase violations ND-15 actually forbids —
    /// <c>ArgentinaCompetitionScore</c>, <c>IsViableInArgentina</c> — fully visible, because
    /// "Argentina" is its own word there.
    ///
    /// Consecutive words are also joined (up to four) so multi-word regions are caught:
    /// <c>IsViableInSouthKorea</c> yields the pair "south"+"korea" → "southkorea". The individual
    /// words stay legitimate on their own.
    /// </summary>
    private static IEnumerable<string> FindCountryTokens(string text)
    {
        var words = WordPattern.Matches(text)
            .Select(match => Normalize(match.Value))
            .Where(word => word.Length > 0)
            .ToList();

        var reported = new HashSet<string>(StringComparer.Ordinal);

        for (var start = 0; start < words.Count; start++)
        {
            var joined = string.Empty;

            for (var length = 0; length < MaxCountryNameWords && start + length < words.Count; length++)
            {
                joined += words[start + length];

                var isSingleWord = length == 0;
                var isViolation = CountryNames.Contains(joined) || (isSingleWord && Demonyms.Contains(joined));

                if (isViolation && reported.Add(joined))
                {
                    yield return joined;
                }
            }
        }
    }

    /// <summary>
    /// Lowercases and strips diacritics and every non-letter, so <c>"Côte d'Ivoire"</c> and
    /// <c>CoteDIvoire</c> reduce to the same comparable token.
    /// </summary>
    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetter(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static HashSet<string> BuildCountryNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;
            try
            {
                region = new RegionInfo(culture.Name);
            }
            catch (ArgumentException)
            {
                // Not every specific culture maps to a region (e.g. some neutral-ish variants).
                continue;
            }

            var normalized = Normalize(region.EnglishName);
            if (normalized.Length >= MinCountryNameLength)
            {
                names.Add(normalized);
            }
        }

        return names;
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
