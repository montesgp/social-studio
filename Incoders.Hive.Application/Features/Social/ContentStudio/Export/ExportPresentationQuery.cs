using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Export;

public sealed record PresentationExportResponse(byte[] PptxBytes, string DownloadFilename);

/// <summary>
/// Builds a client-presentation PPTX deck from a set's reviewed visual items — one slide per visual
/// piece — by reusing the existing offline OpenXML PPTX engine (<see cref="IPptxExportAdapter"/>).
/// Rejected and discarded items are excluded. No new rendering engine is introduced (P4 export).
/// </summary>
public sealed record ExportPresentationQuery(Guid OrganizationId, Guid SetId)
    : IRequest<Result<PresentationExportResponse>>;

public sealed class ExportPresentationQueryHandler
    : IRequestHandler<ExportPresentationQuery, Result<PresentationExportResponse>>
{
    private static readonly ReviewState[] ExcludedStates = [ReviewState.Rejected, ReviewState.Discarded];

    private readonly IContentSetRepository _sets;
    private readonly IContentItemRepository _items;
    private readonly IPptxExportAdapter _pptx;

    public ExportPresentationQueryHandler(
        IContentSetRepository sets,
        IContentItemRepository items,
        IPptxExportAdapter pptx)
    {
        _sets = sets;
        _items = items;
        _pptx = pptx;
    }

    public async Task<Result<PresentationExportResponse>> Handle(ExportPresentationQuery request, CancellationToken ct)
    {
        var set = await _sets.GetByIdAsync(request.OrganizationId, new ContentSetId(request.SetId), ct);
        if (set is null)
        {
            return Result.Failure<PresentationExportResponse>(ContentStudioErrors.SetNotFound);
        }

        var items = await _items.ListBySetAsync(request.OrganizationId, new ContentSetId(request.SetId), ct);

        var envelopes = items
            .Where(i => !ExcludedStates.Contains(i.State) && !string.IsNullOrWhiteSpace(i.Draft.VisualSpecMarkup))
            .SelectMany(i => CompositionSpecJson.ParseList(i.Draft.VisualSpecMarkup))
            .ToList();

        if (envelopes.Count == 0)
        {
            return Result.Failure<PresentationExportResponse>(
                ContentStudioErrors.Validation("No reviewed visual content available to export as a presentation."));
        }

        var build = await _pptx.BuildManyAsync(envelopes, ct);
        if (build.IsFailure)
        {
            return Result.Failure<PresentationExportResponse>(build.Error);
        }

        return Result.Success(new PresentationExportResponse(build.Value, "content-studio-presentation.pptx"));
    }
}
