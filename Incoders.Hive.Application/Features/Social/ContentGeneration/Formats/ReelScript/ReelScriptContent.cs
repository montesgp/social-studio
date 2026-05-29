namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ReelScript;

/// <summary>A single scene in a reel/TikTok script.</summary>
public sealed record ReelScriptScene(
    string Timestamp,
    string Voiceover,
    string OnScreenText);

/// <summary>Structured output schema for reel/TikTok script format generation.</summary>
public sealed record ReelScriptContent(
    string Hook,
    IReadOnlyList<ReelScriptScene> Scenes,
    string CtaText,
    string? SuggestedMusicMood);
