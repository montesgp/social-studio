using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// Exposes the single font contract (Decision F1-F3 — Content Studio editing completeness) to the
/// Web picker: brand fonts first (from the client's active brand manual, if any) plus the bundled
/// system-safe list, and whether the render path can actually draw color emoji. Per R2.2, an
/// unconfigured/missing brand manual still yields a usable picker — the system list alone.
/// </summary>
public sealed record GetRenderCapabilitiesQuery(Guid OrganizationId, Guid ClientId)
    : IRequest<Result<RenderCapabilitiesResponse>>;

public sealed class GetRenderCapabilitiesQueryHandler
    : IRequestHandler<GetRenderCapabilitiesQuery, Result<RenderCapabilitiesResponse>>
{
    private readonly IClientBrandManualRepository _manuals;
    private readonly IFontProvider _fonts;

    public GetRenderCapabilitiesQueryHandler(IClientBrandManualRepository manuals, IFontProvider fonts)
    {
        _manuals = manuals;
        _fonts = fonts;
    }

    public async Task<Result<RenderCapabilitiesResponse>> Handle(
        GetRenderCapabilitiesQuery request, CancellationToken ct)
    {
        var manual = await _manuals.GetActiveByClientAsync(
            new ClientId(request.ClientId), request.OrganizationId, ct);

        var brandTypography = manual?.Typography.Select(t => t.FontFamily).ToArray() ?? Array.Empty<string>();
        var catalog = FontCatalog.For(brandTypography);
        var capability = _fonts.Probe();

        return Result.Success(new RenderCapabilitiesResponse(
            catalog.Select(FontEntryDto.FromDomain).ToList(),
            capability.SupportsColorEmoji));
    }
}
