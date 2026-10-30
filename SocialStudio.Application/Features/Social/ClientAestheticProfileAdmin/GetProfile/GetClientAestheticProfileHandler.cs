using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ProfileAnalysis;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientAestheticProfileAdmin.GetProfile;

/// <summary>
/// client-analysis-data admin panel: resolves the client's latest analysis metadata plus the
/// full decoded aesthetic profile (when the latest analysis is Analyzed). Never 500s on missing
/// or sparse data — degrades to <see cref="ClientAestheticProfileAdminResponse.NoAnalysis"/> or
/// null profile fields instead.
/// </summary>
public sealed class GetClientAestheticProfileHandler
    : IRequestHandler<GetClientAestheticProfileQuery, Result<ClientAestheticProfileAdminResponse>>
{
    private readonly ISocialClientRepository _clients;
    private readonly ISocialClientDirectoryRepository _directory;
    private readonly IProfileAnalysisRepository _analyses;
    private readonly IClientAestheticProfileReader _profileReader;

    public GetClientAestheticProfileHandler(
        ISocialClientRepository clients,
        ISocialClientDirectoryRepository directory,
        IProfileAnalysisRepository analyses,
        IClientAestheticProfileReader profileReader)
    {
        _clients = clients;
        _directory = directory;
        _analyses = analyses;
        _profileReader = profileReader;
    }

    public async Task<Result<ClientAestheticProfileAdminResponse>> Handle(
        GetClientAestheticProfileQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = request.OrganizationId is { } scopedOrganizationId
            ? await _clients.GetByIdAsync(clientId, scopedOrganizationId, cancellationToken)
            : await _directory.GetByIdAcrossOrganizationsAsync(clientId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<ClientAestheticProfileAdminResponse>(ClientErrors.NotFound);
        }

        // The resolved client is authoritative for its own organization, which is what the
        // analysis reads below must be scoped to — for the platform-admin lookup the request
        // carries no organization at all.
        var organizationId = client.OrganizationId;

        var latest = await _analyses.GetLatestForClientAsync(organizationId, request.ClientId, cancellationToken);
        if (latest is null)
        {
            return Result.Success(ClientAestheticProfileAdminResponse.NoAnalysis);
        }

        ClientAestheticProfile? profile = null;
        if (string.Equals(latest.Status, nameof(ProfileAnalysisStatus.Analyzed), StringComparison.Ordinal))
        {
            profile = await _profileReader.GetProfileAsync(organizationId, request.ClientId, cancellationToken);
        }

        return Result.Success(new ClientAestheticProfileAdminResponse(
            HasAnalysis: true,
            AnalysisId: latest.AnalysisId,
            Status: latest.Status,
            CompletedAtUtc: latest.CompletedAtUtc,
            ProfileUrlOrHandle: latest.ProfileUrlOrHandle,
            Profile: profile));
    }
}
