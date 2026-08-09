namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Assembles the material an AI-backed idea source reasons from — the client's strategy, or
/// everything we know about them, or whatever a future source decides to ground itself in.
///
/// WHY THIS EXISTS: "ideas from the client's strategy" and "random ideas from all client knowledge"
/// are NOT two mechanisms. Both ask a model for N topics; they differ only in what they are grounded
/// in and in whether they can serve at all. Splitting that difference out keeps one idea generator
/// instead of two near-identical ones drifting apart.
///
/// RETURNING NULL IS THE DEGRADATION SIGNAL: a client with no strategy authored yet grounds to null,
/// the source contributes nothing, and the chain falls through to the next link.
/// </summary>
public interface IClientIdeaGrounding
{
    /// <summary>Matches the owning source's key, e.g. "client-strategy" or "ai-agent".</summary>
    string GroundingKey { get; }

    /// <summary>
    /// The grounding material, or null/blank when this source has nothing to stand on for this client.
    /// </summary>
    Task<string?> BuildAsync(Guid organizationId, Guid clientId, CancellationToken ct);
}
