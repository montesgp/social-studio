using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.GetProfile;

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
    private readonly IProfileAnalysisRepository _analyses;
    private readonly IClientAestheticProfileReader _profileReader;

    public GetClientAestheticProfileHandler(
        ISocialClientRepository clients,
        IProfileAnalysisRepository analyses,
        IClientAestheticProfileReader profileReader)
    {
        _clients = clients;
        _analyses = analyses;
        _profileReader = profileReader;
    }

    public async Task<Result<ClientAestheticProfileAdminResponse>> Handle(
        GetClientAestheticProfileQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<ClientAestheticProfileAdminResponse>(ClientErrors.NotFound);
        }

        var latest = await _analyses.GetLatestForClientAsync(request.OrganizationId, request.ClientId, cancellationToken);
        if (latest is null)
        {
            return Result.Success(ClientAestheticProfileAdminResponse.NoAnalysis);
        }

        ClientAestheticProfile? profile = null;
        if (string.Equals(latest.Status, nameof(ProfileAnalysisStatus.Analyzed), StringComparison.Ordinal))
        {
            profile = await _profileReader.GetProfileAsync(request.OrganizationId, request.ClientId, cancellationToken);
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
