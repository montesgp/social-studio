namespace SocialStudio.Infrastructure.Social.Discovery;

/// <summary>
/// Configuration for the LOCAL discovery seed importer, bound from the section
/// <c>Social:DiscoverySeedImport</c>.
///
/// WHY THIS EXISTS. Several Apify niche-search runs were paid for and their output discarded, leaving
/// the candidate bank empty and every tool built on it (the shortlist, the AI Analysis page) with
/// nothing to show. The captured output of those runs is committed under
/// <see cref="DirectoryPath"/> and this importer replays it into the bank so local work has real
/// data without buying it again.
///
/// It is LOCAL TEST DATA, not product content: both <see cref="Enabled"/> and the Development-only
/// rule must hold before a single row is written. Two independent switches, deliberately — an
/// environment misconfiguration alone must not be enough to fill a real tenant's bank with a
/// snapshot of somebody else's niche.
/// </summary>
public sealed class DiscoverySeedImportOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Social:DiscoverySeedImport";

    /// <summary>
    /// Master switch. Default <c>false</c>: the importer ships wired but inert, so it only ever runs
    /// where an operator asked for it.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where the captured envelopes live. A relative path is probed against the host content root
    /// first and the application base directory second, so it resolves both when the API is run from
    /// its project directory and when it is run from a published output folder.
    /// </summary>
    public string DirectoryPath { get; set; } = Path.Combine("seed-data", "discovery");

    /// <summary>
    /// The organization whose bank receives the seed. The bank is organization-scoped and there is no
    /// safe way to infer the target, so when this is absent the importer falls back to the single
    /// active organization ONLY if exactly one exists — never fanning the same rows across tenants.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// Escape hatch for a non-Development host that genuinely wants the seed (a throwaway staging box
    /// an operator is demoing from). Default <c>false</c>, so the importer is inert outside
    /// Development no matter what <see cref="Enabled"/> says.
    /// </summary>
    public bool AllowOutsideDevelopment { get; set; }
}
