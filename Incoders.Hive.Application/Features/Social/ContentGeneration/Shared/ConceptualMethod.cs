namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Structured conceptual art-direction method captured from a client's visual corpus.
/// Pairs a deterministic <see cref="ArtDirectionMethod"/> enum member (for routing) with
/// free-text signals that preserve nuance without letting detection drift run-to-run.
/// CACD-PA-01; OQ-1.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ArtDirectionMethod.Unknown"/> or a null <see cref="ConceptualMethod"/> means
/// cold-start: <c>ImagePromptWriter</c> runs the existing emulate-style instruction unchanged.
/// </para>
/// <para>Pure Application value object — no Infrastructure imports.</para>
/// </remarks>
/// <param name="Method">Deterministic art-direction enum token. Unknown = cold-start / undetected.</param>
/// <param name="Rationale">How the client turns a message into an image, in their own terms.</param>
/// <param name="Cues">
/// Observable visual cues extracted from the client's feed
/// (e.g. "mono-color saturated backdrop", "object-as-metaphor", "staged human subject").
/// </param>
public sealed record ConceptualMethod(
    ArtDirectionMethod Method,
    string Rationale,
    IReadOnlyList<string> Cues);
