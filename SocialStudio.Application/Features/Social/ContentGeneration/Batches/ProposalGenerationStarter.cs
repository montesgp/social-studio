using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.ContentGeneration.Batches;
using SocialStudio.Domain.Social.SingleAnalyzer;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Batches;

/// <summary>
/// Turns the proposals an operator selected into queued generation work.
///
/// Each proposal is persisted as its own synthesized analysis. The pipeline reads a piece's context
/// from an analysis and fails the job outright without one, so rather than teaching that pipeline a
/// second notion of context, the proposal is expressed in the vocabulary it already speaks. See
/// <see cref="IdeaAnalysisSynthesizer"/> — the synthesized record is explicitly low-confidence and
/// carries its provenance, so nothing downstream mistakes it for a real content analysis.
/// </summary>
public sealed class ProposalGenerationStarter : IProposalGenerationStarter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Placeholder origin for an analysis that came from a proposal rather than from a scraped URL.
    /// The column is non-nullable, and leaving a real-looking URL here would suggest content was
    /// fetched from it.
    /// </summary>
    private const string SyntheticSourceUrl = "hive://proposal";

    private readonly ISingleContentAnalysisRepository _analyses;
    private readonly IContentGenerationJobRepository _jobs;
    private readonly IContentGenerationBatchRepository _batches;
    private readonly IContentGenerationJobQueue _queue;

    public ProposalGenerationStarter(
        ISingleContentAnalysisRepository analyses,
        IContentGenerationJobRepository jobs,
        IContentGenerationBatchRepository batches,
        IContentGenerationJobQueue queue)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(queue);

        _analyses = analyses;
        _jobs = jobs;
        _batches = batches;
        _queue = queue;
    }

    public async Task<ProposalGenerationStarted> StartAsync(
        ProposalGenerationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Proposals.Count == 0)
        {
            throw new ArgumentException("At least one proposal must be selected.", nameof(request));
        }

        if (request.Formats.Count == 0)
        {
            throw new ArgumentException("At least one format must be requested.", nameof(request));
        }

        var now = DateTime.UtcNow;
        var batchId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        // One analysis per PROPOSAL, not per piece: every format built from a topic shares its context,
        // which is also what lets the content bank group them under one source later.
        var analysisIdByProposal = new List<Guid>(request.Proposals.Count);

        foreach (var proposal in request.Proposals)
        {
            var analysisId = Guid.NewGuid();
            var analysis = IdeaAnalysisSynthesizer.Synthesize(proposal.Title, proposal.Summary, proposal.Reference);

            await _analyses.AddAsync(
                new SingleContentAnalysisRecord(
                    Id: analysisId,
                    OrganizationId: request.OrganizationId,
                    ClientId: request.ClientId,
                    UserId: Guid.Empty,
                    SourceUrl: proposal.Reference ?? SyntheticSourceUrl,
                    NormalizedUrl: null,
                    SourcePlatformKey: null,
                    // Analyzed, because the context is complete and ready to generate from. Any other
                    // status would make the processor treat the piece as not yet ready.
                    Status: SingleContentAnalysisStatus.Analyzed,
                    ContentSnapshotJson: null,
                    AnalysisJson: JsonSerializer.Serialize(analysis, JsonOptions),
                    ProviderDiagnosticsJson: null,
                    FailureCode: null,
                    FailureMessageKey: null,
                    CreatedAtUtc: now,
                    UpdatedAtUtc: now,
                    CompletedAtUtc: now),
                ct);

            analysisIdByProposal.Add(analysisId);
        }

        var composition = request.Formats
            .Select(format => new ContentGenerationBatchEntry(format, request.Proposals.Count))
            .ToList();

        var totalPieces = request.Proposals.Count * request.Formats.Count;

        await _batches.InsertAsync(
            new ContentGenerationBatch(
                Id: batchId,
                OrganizationId: request.OrganizationId,
                ClientId: request.ClientId,
                // A batch spanning several proposals has no single analysis; the per-piece jobs carry
                // the real ones.
                AnalysisId: Guid.Empty,
                Status: "Queued",
                Composition: composition,
                TotalPieces: totalPieces,
                CorrelationId: correlationId,
                CreatedAt: now,
                UpdatedAt: now,
                CompletedAt: null),
            ct);

        var jobIds = new List<Guid>(totalPieces);
        var pieceIndex = 0;

        for (var proposal = 0; proposal < request.Proposals.Count; proposal++)
        {
            foreach (var format in request.Formats)
            {
                var jobId = Guid.NewGuid();
                jobIds.Add(jobId);

                await _jobs.InsertAsync(
                    new ContentGenerationJob(
                        Id: jobId,
                        OrganizationId: request.OrganizationId,
                        ClientId: request.ClientId,
                        AnalysisId: analysisIdByProposal[proposal],
                        // Exactly one format per job: a result row is keyed (JobId, FormatKey), so a
                        // second format in the same job would overwrite the first.
                        RequestedFormats: [format],
                        Status: "Queued",
                        StatusReason: null,
                        ErrorCode: null,
                        BrandSnapshotJson: "{}",
                        ManifestJson: null,
                        CorrelationId: correlationId,
                        CreatedAt: now,
                        UpdatedAt: now,
                        StartedAt: null,
                        CompletedAt: null,
                        RegenerationObjection: null,
                        BatchId: batchId,
                        BatchPieceIndex: pieceIndex),
                    ct);

                pieceIndex++;
            }
        }

        // Enqueue only once every piece exists: a worker that picked up piece 0 mid-write would read a
        // batch whose total is still growing and report progress against a moving target.
        foreach (var jobId in jobIds)
        {
            await _queue.EnqueueAsync(jobId, ct);
        }

        return new ProposalGenerationStarted(batchId, totalPieces);
    }
}
