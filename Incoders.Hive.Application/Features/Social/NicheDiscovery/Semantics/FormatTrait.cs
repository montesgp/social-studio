namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Semantics;

/// <summary>
/// Closed enum of recognized production formats (design §8, N4 — a label crossing the AI boundary is
/// only ever a closed enum). Feeds <c>AutomationSuitability</c> (design §7.1): an empty
/// <c>FormatTrait[]</c> — every AI-proposed label unrecognized, or the lexical fallback path, which
/// never proposes any — makes <c>AutomationSuitability</c> <c>NotReported</c>, never guessed.
/// </summary>
public enum FormatTrait
{
    TalkingHead = 0,
    VoiceoverOverStockFootage = 1,
    ScreenRecording = 2,
    TextOverlayOnly = 3,
    Animation = 4,
    LiveActionOriginalFootage = 5,
}
