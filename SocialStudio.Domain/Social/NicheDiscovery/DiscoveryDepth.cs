namespace SocialStudio.Domain.Social.NicheDiscovery;

/// <summary>
/// Operator-chosen depth bounding provider API calls and AI completions for one
/// <see cref="DiscoveryRun"/>. The concrete ceiling table lives in Infrastructure (design §5.3).
/// </summary>
public enum DiscoveryDepth
{
    Quick = 1,
    Standard = 2,
    Deep = 3,
}
