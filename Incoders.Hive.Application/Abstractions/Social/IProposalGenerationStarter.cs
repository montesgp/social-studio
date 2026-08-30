namespace Incoders.Hive.Application.Abstractions.Social;

/// <summary>One proposal the operator chose to generate content from.</summary>
public sealed record SelectedProposal(
    string Title,
    string? Summary,
    string? Reference);

/// <summary>
/// Turns the operator's chosen proposals into real background generation work.
///
/// This is the seam between the Content Generator's two halves: discovery proposes, a human selects,
/// and this starts the pieces. It exists as a port so the workflow service — which holds sessions in
/// memory and knows nothing about jobs, analyses or the content bank — does not grow those concerns.
/// </summary>
public interface IProposalGenerationStarter
{
    /// <summary>
    /// Queues one generation job per (proposal x format). Returns the batch that tracks them.
    /// </summary>
    Task<ProposalGenerationStarted> StartAsync(
        ProposalGenerationRequest request,
        CancellationToken ct = default);
}

/// <param name="Formats">
/// Catalog format keys to build for EACH selected proposal. Coming from the operator's composition
/// choice rather than being fixed here, so the same proposal can yield a script, a carousel, or both.
/// </param>
public sealed record ProposalGenerationRequest(
    Guid OrganizationId,
    Guid ClientId,
    IReadOnlyList<SelectedProposal> Proposals,
    IReadOnlyList<string> Formats);

public sealed record ProposalGenerationStarted(
    Guid BatchId,
    int TotalPieces);
