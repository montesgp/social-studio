namespace SocialStudio.Domain.Social.ContentPlans;

/// <summary>
/// Audit-trace status of a provenance-decorated node.
/// Status is NEVER a blocking gate — it records history only.
/// Transitions:
///   Empty   -> Draft      (user starts typing)        Source=Manual
///   Empty   -> Generated  (AI fills the node)          Source=AI
///   Draft   -> Draft      (continued manual edits)     Source=Manual
///   Draft   -> Generated  (AI regenerates over manual) Source=AI
///   Generated -> Edited   (human tweaks AI output)     Source unchanged
///   Generated -> Generated (AI regenerates)            Source=AI
///   Edited  -> Edited     (continued manual edits)     Source unchanged
///   Edited  -> Generated  (AI regenerates over edits)  Source=AI
/// </summary>
public enum ProvenanceStatus
{
    /// <summary>Node exists structurally but has no content yet.</summary>
    Empty = 0,

    /// <summary>Manually authored content, work-in-progress (Source = Manual).</summary>
    Draft = 1,

    /// <summary>Content was produced by AI (Source = AI), untouched since.</summary>
    Generated = 2,

    /// <summary>AI-generated content subsequently modified by a human.</summary>
    Edited = 3,
}
