using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientAestheticProfileAdmin.GetProfile;

/// <summary>
/// client-analysis-data admin panel: fetches the client's most recent profile analysis
/// (status/completion/scrape target) together with the FULL decoded aesthetic profile —
/// palette, background pattern, style descriptors, motifs, reference images, etc. — so the
/// admin panel can display everything captured, including gaps (missing/empty fields).
/// </summary>
/// <param name="OrganizationId">
/// Organization the client lookup is scoped to. <c>null</c> resolves across every organization and
/// is reserved for platform admins, matching the by-id client endpoint. Downstream analysis reads
/// always use the resolved client's own organization, never this value.
/// </param>
public sealed record GetClientAestheticProfileQuery(
    Guid? OrganizationId,
    Guid ClientId) : IRequest<Result<ClientAestheticProfileAdminResponse>>;
