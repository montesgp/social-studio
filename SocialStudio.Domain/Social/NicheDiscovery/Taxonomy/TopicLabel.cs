namespace SocialStudio.Domain.Social.NicheDiscovery.Taxonomy;

/// <summary>
/// One AI-identified topic/entity label persisted on a <c>NicheOpportunity</c> (design §8).
/// A free-text label, never a scorer input (N4 constrains only enum labels crossing into scoring;
/// topic labels are display-only).
/// </summary>
public readonly record struct TopicLabel(string Value);
