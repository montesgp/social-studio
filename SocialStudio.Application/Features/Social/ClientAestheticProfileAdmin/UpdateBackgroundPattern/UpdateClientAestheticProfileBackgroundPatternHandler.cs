using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ProfileAnalysis;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientAestheticProfileAdmin.UpdateBackgroundPattern;

/// <summary>
/// client-analysis-data admin panel: persists an admin-corrected <c>BackgroundPattern</c> onto
/// the client's latest Analyzed profile analysis. Round-trips the FULL stored aesthetic JSON via
/// <see cref="AestheticProfilePersistence.WithBackgroundPattern"/> so palette/motifs/reference
/// images/etc. captured by the extractor are never lost — only BackgroundPattern (and its
/// derived TypicalSlideCount) change.
/// </summary>
public sealed class UpdateClientAestheticProfileBackgroundPatternHandler
    : IRequestHandler<UpdateClientAestheticProfileBackgroundPatternCommand, Result<UpdateClientAestheticProfileBackgroundPatternResponse>>
{
    private readonly ISocialClientRepository _clients;
    private readonly ISocialClientDirectoryRepository _directory;
    private readonly IProfileAnalysisRepository _analyses;

    public UpdateClientAestheticProfileBackgroundPatternHandler(
        ISocialClientRepository clients,
        ISocialClientDirectoryRepository directory,
        IProfileAnalysisRepository analyses)
    {
        _clients = clients;
        _directory = directory;
        _analyses = analyses;
    }

    public async Task<Result<UpdateClientAestheticProfileBackgroundPatternResponse>> Handle(
        UpdateClientAestheticProfileBackgroundPatternCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = request.OrganizationId is { } scopedOrganizationId
            ? await _clients.GetByIdAsync(clientId, scopedOrganizationId, cancellationToken)
            : await _directory.GetByIdAcrossOrganizationsAsync(clientId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<UpdateClientAestheticProfileBackgroundPatternResponse>(ClientErrors.NotFound);
        }

        // The resolved client owns the organization the analysis write is scoped to; the
        // platform-admin lookup carries none on the request.
        var organizationId = client.OrganizationId;

        var latest = await _analyses.GetLatestForClientAsync(organizationId, request.ClientId, cancellationToken);
        if (latest is null || !string.Equals(latest.Status, nameof(ProfileAnalysisStatus.Analyzed), StringComparison.Ordinal))
        {
            return Result.Failure<UpdateClientAestheticProfileBackgroundPatternResponse>(
                ProfileAnalysisErrors.NoCompletedAnalysis);
        }

        // Validator already guarantees every entry parses; parse again here so the handler
        // owns the actual enum values it writes (no reliance on validator side effects).
        var kinds = request.BackgroundPattern
            .Select(v => Enum.Parse<SlideBackgroundKind>(v, ignoreCase: true))
            .ToArray();

        var patchedJson = AestheticProfilePersistence.WithBackgroundPattern(latest.AestheticProfileJson, kinds);

        await _analyses.SetAestheticProfileJsonAsync(
            latest.AnalysisId, organizationId, patchedJson, cancellationToken);

        return Result.Success(new UpdateClientAestheticProfileBackgroundPatternResponse(
            latest.AnalysisId,
            kinds.Select(k => k.ToString()).ToArray(),
            kinds.Length));
    }
}
