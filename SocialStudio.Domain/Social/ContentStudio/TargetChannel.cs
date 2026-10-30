namespace SocialStudio.Domain.Social.ContentStudio;

/// <summary>
/// The destination network and placement a content piece targets (e.g. instagram/carousel,
/// tiktok/reel, youtube/short). Drives preview channel constraints and the publication handoff.
/// Network/placement are open strings validated against an Application-side allowlist.
/// </summary>
public sealed record TargetChannel(
    string Network,
    string Placement);
