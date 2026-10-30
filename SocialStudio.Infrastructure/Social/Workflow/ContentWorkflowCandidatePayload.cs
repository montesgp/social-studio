using System.Text.Json;

namespace SocialStudio.Infrastructure.Social.Workflow;

/// <summary>
/// One candidate's discovery payload, serialized into <c>content_workflow_candidates.payload_json</c>.
/// Infrastructure-internal shape only — Application/Domain never sees raw JSON (see
/// <see cref="Domain.Social.Workflow.WorkflowCandidate.SourceUrl"/>, which is derived from
/// <see cref="VideoUrl"/> at hydration time). Shared by <see cref="EfContentWorkflowService"/> (read-model
/// projection for the status/results screens) and
/// <see cref="Persistence.Postgres.Repositories.EfContentWorkflowSessionRepository"/> (domain hydration)
/// so there is exactly one JSON shape for this payload, not two that could silently drift apart.
/// </summary>
internal sealed record ContentWorkflowCandidatePayload(
    string Title,
    string NetworkSource,
    string SearchCriterion,
    string StrategyMode,
    string VideoUrl,
    string ThumbnailUrl,
    string Summary,
    int Views,
    int Likes,
    int Comments)
{
    public static ContentWorkflowCandidatePayload Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, 0);

    public static ContentWorkflowCandidatePayload Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<ContentWorkflowCandidatePayload>(json) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}
