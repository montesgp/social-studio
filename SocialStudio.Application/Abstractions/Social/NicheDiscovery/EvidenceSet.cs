namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

public sealed record EvidenceSet(IReadOnlyList<ChannelEvidence> Channels, IReadOnlyList<VideoEvidence> Videos);
