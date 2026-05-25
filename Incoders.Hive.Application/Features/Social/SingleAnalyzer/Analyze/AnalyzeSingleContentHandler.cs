using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;

public sealed class AnalyzeSingleContentHandler
    : IRequestHandler<AnalyzeSingleContentCommand, Result<SingleContentAnalysisResponse>>
{
    private readonly ISingleContentAnalyzer _analyzer;

    public AnalyzeSingleContentHandler(ISingleContentAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public Task<Result<SingleContentAnalysisResponse>> Handle(
        AnalyzeSingleContentCommand request,
        CancellationToken cancellationToken) =>
        _analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                request.OrganizationId,
                request.UserId,
                request.Url,
                request.ClientId,
                request.PreferredExtractionProviderKey),
            cancellationToken);
}
