using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ClientBrandManuals.Converters;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientBrandManuals.Update;

public sealed class UpdateClientBrandManualHandler
    : IRequestHandler<UpdateClientBrandManualCommand, Result<ClientBrandManualResponse>>
{
    private readonly IClientBrandManualRepository _manuals;
    private readonly ISocialClientRepository _clients;
    private readonly IClock _clock;

    public UpdateClientBrandManualHandler(
        IClientBrandManualRepository manuals,
        ISocialClientRepository clients,
        IClock clock)
    {
        _manuals = manuals;
        _clients = clients;
        _clock = clock;
    }

    public async Task<Result<ClientBrandManualResponse>> Handle(
        UpdateClientBrandManualCommand request,
        CancellationToken cancellationToken)
    {
        // Defensive client check (ADR 11)
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        var manual = await _manuals.GetByIdAsync(
            new ClientBrandManualId(request.Id), request.OrganizationId, cancellationToken);
        if (manual is null)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        // Defensive ClientId check (Resolution 1)
        if (manual.ClientId != clientId)
        {
            return Result.Failure<ClientBrandManualResponse>(ClientBrandManualErrors.NotFound);
        }

        // Convert DTOs to domain records
        var paletteResult = BrandManualDtoConverters.ToColorEntries(request.ColorPalette);
        if (paletteResult.IsFailure) return Result.Failure<ClientBrandManualResponse>(paletteResult.Error);

        var logosResult = BrandManualDtoConverters.ToLogoEntries(request.Logos);
        if (logosResult.IsFailure) return Result.Failure<ClientBrandManualResponse>(logosResult.Error);

        var bgResult = BrandManualDtoConverters.ToBackgroundEntries(request.BackgroundTemplates);
        if (bgResult.IsFailure) return Result.Failure<ClientBrandManualResponse>(bgResult.Error);

        var typoResult = BrandManualDtoConverters.ToTypographyEntries(request.Typography);
        if (typoResult.IsFailure) return Result.Failure<ClientBrandManualResponse>(typoResult.Error);

        var updateResult = manual.Update(
            request.Title,
            paletteResult.Value,
            logosResult.Value,
            bgResult.Value,
            typoResult.Value,
            request.VoiceAndTone,
            request.Notes,
            _clock.UtcNow);

        if (updateResult.IsFailure)
        {
            return Result.Failure<ClientBrandManualResponse>(updateResult.Error);
        }

        _manuals.Update(manual);
        await _manuals.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientBrandManualResponse.FromDomain(manual));
    }
}
