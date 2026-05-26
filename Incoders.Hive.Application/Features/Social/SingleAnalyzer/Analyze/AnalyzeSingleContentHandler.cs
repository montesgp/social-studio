using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;

/// <summary>
/// Starts an analysis: validates + pre-flights + creates the record (Requested), then enqueues the
/// heavy work to run in the background. Returns immediately with the analysis id/status so the
/// client can poll — the long extraction + AI run is decoupled from this HTTP request.
/// </summary>
public sealed class AnalyzeSingleContentHandler
    : IRequestHandler<AnalyzeSingleContentCommand, Result<SingleAnalyzerStatusResponse>>
{
    private readonly ISingleContentAnalyzer _analyzer;
    private readonly ISingleAnalysisJobQueue _queue;

    public AnalyzeSingleContentHandler(ISingleContentAnalyzer analyzer, ISingleAnalysisJobQueue queue)
    {
        _analyzer = analyzer;
        _queue = queue;
    }

    public async Task<Result<SingleAnalyzerStatusResponse>> Handle(
        AnalyzeSingleContentCommand request,
        CancellationToken cancellationToken)
    {
        var started = await _analyzer.StartAsync(
            new SingleContentAnalysisRequest(
                request.OrganizationId,
                request.UserId,
                request.Url,
                request.ClientId,
                request.PreferredExtractionProviderKey),
            cancellationToken);

        if (started.IsFailure)
        {
            return started;
        }

        await _queue.EnqueueAsync(
            new SingleAnalysisJob(started.Value.Id, started.Value.OrganizationId),
            cancellationToken);

        return started;
    }
}

/// <summary>Reads the current status/result of an analysis for polling.</summary>
public sealed class GetSingleContentAnalysisHandler
    : IRequestHandler<GetSingleContentAnalysisQuery, Result<SingleAnalyzerStatusResponse>>
{
    private readonly ISingleContentAnalyzer _analyzer;

    public GetSingleContentAnalysisHandler(ISingleContentAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public Task<Result<SingleAnalyzerStatusResponse>> Handle(
        GetSingleContentAnalysisQuery request,
        CancellationToken cancellationToken) =>
        _analyzer.GetAsync(request.AnalysisId, request.OrganizationId, cancellationToken);
}
