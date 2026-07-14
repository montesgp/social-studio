using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.GetProfile;

/// <summary>
/// client-analysis-data admin panel: fetches the client's most recent profile analysis
/// (status/completion/scrape target) together with the FULL decoded aesthetic profile —
/// palette, background pattern, style descriptors, motifs, reference images, etc. — so the
/// admin panel can display everything captured, including gaps (missing/empty fields).
/// </summary>
public sealed record GetClientAestheticProfileQuery(
    Guid OrganizationId,
    Guid ClientId) : IRequest<Result<ClientAestheticProfileAdminResponse>>;
