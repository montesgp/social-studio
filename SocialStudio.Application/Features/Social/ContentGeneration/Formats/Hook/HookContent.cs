namespace SocialStudio.Application.Features.Social.ContentGeneration.Formats.Hook;

/// <summary>Structured output schema for hook format generation.</summary>
public sealed record HookContent(
    string PrimaryHook,
    IReadOnlyList<string> Alternates,
    string Tone,
    string Pattern);
