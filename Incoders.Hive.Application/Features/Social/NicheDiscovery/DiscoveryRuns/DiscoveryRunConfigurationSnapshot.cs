using System.Text.Json;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// The shape captured into <c>DiscoveryRun.ConfigurationSnapshotJson</c> at run creation (design §9,
/// ND-01): "the resolved origin mapping, depth ceiling, and other execution parameters". Operator seed
/// terms and an optional per-run weight override (design §7.1: "Optional per-run override on the start
/// request") are exactly that kind of execution parameter — the aggregate has no dedicated column for
/// either, by design (mirrors <c>ProfileAnalysis.RawPostsDatasetJson</c>'s opaque-JSON discipline), so
/// they round-trip through this one snapshot instead of new aggregate fields.
/// </summary>
public sealed record DiscoveryRunConfigurationSnapshot(
    IReadOnlyList<string> SeedTerms,
    OpportunityScoreWeights? Weights = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Never throws: an empty or unparsable snapshot resolves to no seed terms and the default weights.</summary>
    public static DiscoveryRunConfigurationSnapshot FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new DiscoveryRunConfigurationSnapshot([]);
        }

        try
        {
            return JsonSerializer.Deserialize<DiscoveryRunConfigurationSnapshot>(json, JsonOptions)
                   ?? new DiscoveryRunConfigurationSnapshot([]);
        }
        catch (JsonException)
        {
            return new DiscoveryRunConfigurationSnapshot([]);
        }
    }
}
