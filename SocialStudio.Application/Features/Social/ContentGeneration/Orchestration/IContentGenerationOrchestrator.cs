namespace SocialStudio.Application.Features.Social.ContentGeneration.Orchestration;

/// <summary>
/// Application port for running the content package generation pipeline.
/// Coordinates per-format handlers and aggregates the overall status.
/// </summary>
public interface IContentGenerationOrchestrator
{
    /// <summary>
    /// Runs generation for all requested formats in the given request.
    /// Never throws — format failures are captured in <see cref="OrchestratorResult"/>.
    /// </summary>
    Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken cancellationToken);
}
