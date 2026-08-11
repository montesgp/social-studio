using System.Text.RegularExpressions;

namespace Incoders.Hive.Tests.Architecture;

/// <summary>
/// CD-12/D13: no literal actor id may ever exist in shipped source — actor resolution is exclusively
/// configuration-driven (design §11 Guard row, §13 risk).
///
/// Written RED first, before any niche-search code exists, so "no forced actor" is enforceable rather
/// than aspirational.
///
/// NARROWED, not repository-wide: a naive 17-char-alphanumeric-token regex applied across the whole
/// `Incoders.Hive.Application`/`Incoders.Hive.Infrastructure` trees false-positives badly — it matches
/// XML doc comment tokens like `&lt;see cref="..."/&gt;` all over the codebase (verified during this
/// apply pass). The design's own caveat (§6.1 task text) permits tightening the guard to the actual
/// Apify call-site files instead of deleting it. A noisy guard that gets disabled later protects
/// nothing; a targeted one does.
/// </summary>
public sealed class NoActorLiteralGuardTests
{
    // Files that actually dispatch to Apify, i.e. the only places a literal actor id could plausibly
    // leak in as a "temporary" convenience.
    private static readonly string[] GuardedRelativePaths =
    [
        "Incoders.Hive.Infrastructure/Social/Providers/ApifyProfileScrapeResolver.cs",
        "Incoders.Hive.Infrastructure/Social/Providers/ApifyNicheSearchResolver.cs",
        "Incoders.Hive.Infrastructure/Social/Providers/ApifyActorMappingLookup.cs",
        "Incoders.Hive.Infrastructure/Social/Providers/Apify/TikTokNicheSearchAdapter.cs",
        // The actor CATALOG client also talks to Apify. It must discover actors from the account,
        // never seed or filter them against a baked-in id.
        "Incoders.Hive.Infrastructure/Social/Providers/ApifyActorCatalogClient.cs",
    ];

    // Apify actor ids are either a bare 17-character alphanumeric token (legacy convention) or a
    // "vendor/actor-name" slug (current Apify Store convention, e.g. "clockworks/free-tiktok-scraper").
    //
    // Matched ONLY inside C# string-literal quotes, deliberately — a bare (unquoted) token pattern
    // matches ordinary C# identifiers (PascalCase/camelCase method and parameter names are routinely
    // exactly 17 characters: `CancellationToken`, `IntegrationStatus`, `actorMappingsJson`, …) and XML
    // doc `&lt;see cref="..."/&gt;` tokens across the codebase — confirmed while narrowing this guard.
    // An actor id can only ever leak in as a STRING VALUE, never as a bare code token, so anchoring the
    // match to quoted content removes that entire false-positive class without weakening the guard.
    private static readonly Regex QuotedBareTokenPattern = new(@"""([A-Za-z0-9]{17})""", RegexOptions.Compiled);

    // Requires the segment after the slash to be a multi-word, hyphen/underscore-joined name (how real
    // Apify actor slugs look: "clockworks/free-tiktok-scraper", "get-leads/all-in-one-instagram-scraper")
    // rather than a single bare word — this is what keeps ordinary quoted values like "application/json"
    // out of the match without an explicit denylist.
    private static readonly Regex VendorSlugPattern = new(
        @"""[a-z0-9][a-z0-9_-]*/[a-z0-9]+(?:[-_][a-z0-9]+)+""",
        RegexOptions.Compiled);

    [Fact]
    public void GuardedApifyCallSites_ContainNoActorIdShapedLiteral()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (var relativePath in GuardedRelativePaths)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                // A guarded file that does not exist yet (still RED) cannot contain a literal — nothing
                // to assert here; the file's absence is covered by its own feature test.
                continue;
            }

            // XML doc comment lines (`/// <see cref="Type"/>`) are not source string literals and are
            // exactly the false-positive class the design flagged (§6.1 task text) — stripped before
            // matching rather than excluded via a growing denylist.
            var text = string.Join(
                '\n',
                File.ReadAllLines(fullPath).Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

            foreach (Match match in QuotedBareTokenPattern.Matches(text))
            {
                offenders.Add($"{relativePath}: quoted actor-id-shaped literal {match.Value}");
            }

            foreach (Match match in VendorSlugPattern.Matches(text))
            {
                offenders.Add($"{relativePath}: vendor-actor-slug-shaped literal {match.Value}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "No actor id may ever be a literal in an Apify call-site file (CD-12/D13). Offenders:\n"
                + string.Join('\n', offenders));
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
