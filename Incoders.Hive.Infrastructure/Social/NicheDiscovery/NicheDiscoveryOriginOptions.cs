namespace Incoders.Hive.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// Configuration-bound base address for the YouTube Data API v3 origin (design §5.2). Bound from
/// <c>IOptions&lt;NicheDiscoveryOriginOptions&gt;</c> — never a code literal. A null/blank
/// <see cref="BaseAddress"/> degrades the origin to <c>Unconfigured</c>, never a throw.
/// </summary>
public sealed class NicheDiscoveryOriginOptions
{
    public const string SectionName = "NicheDiscovery:YouTubeDataApi";

    public string? BaseAddress { get; set; }
}
