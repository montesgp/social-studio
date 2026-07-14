using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.UpdateBackgroundPattern;

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
    private readonly IProfileAnalysisRepository _analyses;

    public UpdateClientAestheticProfileBackgroundPatternHandler(
        ISocialClientRepository clients,
        IProfileAnalysisRepository analyses)
    {
        _clients = clients;
        _analyses = analyses;
    }

    public async Task<Result<UpdateClientAestheticProfileBackgroundPatternResponse>> Handle(
        UpdateClientAestheticProfileBackgroundPatternCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<UpdateClientAestheticProfileBackgroundPatternResponse>(ClientErrors.NotFound);
        }

        var latest = await _analyses.GetLatestForClientAsync(request.OrganizationId, request.ClientId, cancellationToken);
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
            latest.AnalysisId, request.OrganizationId, patchedJson, cancellationToken);

        return Result.Success(new UpdateClientAestheticProfileBackgroundPatternResponse(
            latest.AnalysisId,
            kinds.Select(k => k.ToString()).ToArray(),
            kinds.Length));
    }
}
