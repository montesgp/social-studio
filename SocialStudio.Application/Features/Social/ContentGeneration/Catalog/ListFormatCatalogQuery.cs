using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentFormats;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Catalog;

/// <summary>
/// Query: returns all catalog deliverable entries (including auxiliaries) projected to DTOs.
/// Satisfies: ADR-6, REQ-09, SCENARIO-12, SCENARIO-13.
/// </summary>
public sealed record ListFormatCatalogQuery : IRequest<Result<ListFormatCatalogResponse>>;

/// <summary>
/// Response for <see cref="ListFormatCatalogQuery"/>.
/// </summary>
public sealed record ListFormatCatalogResponse(IReadOnlyList<FormatCatalogEntryDto> Entries);

/// <summary>
/// Handles <see cref="ListFormatCatalogQuery"/> by projecting <see cref="FormatCatalog.All"/>
/// into <see cref="FormatCatalogEntryDto"/> list. No infrastructure dependency — pure catalog
/// projection.
/// </summary>
public sealed class ListFormatCatalogQueryHandler : IRequestHandler<ListFormatCatalogQuery, Result<ListFormatCatalogResponse>>
{
    public Task<Result<ListFormatCatalogResponse>> Handle(
        ListFormatCatalogQuery request,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Success(new ListFormatCatalogResponse(Entries: ExecuteQuery())));

    /// <summary>
    /// Synchronous projection used by tests without MediatR plumbing.
    /// </summary>
    public IReadOnlyList<FormatCatalogEntryDto> ExecuteQuery()
        => FormatCatalog.All()
            .Select(def => new FormatCatalogEntryDto(
                FormatKey: def.FormatKey,
                Surface: def.Surface.ToString(),
                Layout: def.Layout?.ToString(),
                Medium: def.Medium.ToString(),
                DisplayLabel: def.DisplayLabel,
                IsMultiAsset: def.IsMultiAsset,
                IsAuxiliary: def.IsAuxiliary))
            .ToList();
}
