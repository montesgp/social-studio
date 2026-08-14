namespace Incoders.Hive.Domain.Social.NicheDiscovery;

/// <summary>
/// Identity of a <see cref="DiscoveryRun"/>. Mirrors <c>NicheId.cs</c>.
/// </summary>
public readonly record struct DiscoveryRunId(Guid Value)
{
    public static DiscoveryRunId New() => new(Guid.NewGuid());
}
