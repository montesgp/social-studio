using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Converters;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals.Create;

public sealed class CreateClientBrandManualHandler
    : IRequestHandler<CreateClientBrandManualCommand, Result<ClientBrandManualResponse>>
{
    private readonly IClientBrandManualRepository _manuals;
    private readonly ISocialClientRepository _clients;
    private readonly IClock _clock;

    public CreateClientBrandManualHandler(
        IClientBrandManualRepository manuals,
        ISocialClientRepository clients,
        IClock clock)
    {
        _manuals = manuals;
        _clients = clients;
        _clock = clock;
    }

    public async Task<Result<ClientBrandManualResponse>> Handle(
        CreateClientBrandManualCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);
        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, cancellationToken);
        if (client is null)
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

        // Auto-deactivate prior active manual (ADR 7)
        var existing = await _manuals.GetActiveByClientAsync(clientId, request.OrganizationId, cancellationToken);
        if (existing is not null)
        {
            var deactivateResult = existing.Deactivate(_clock.UtcNow);
            if (deactivateResult.IsFailure) return Result.Failure<ClientBrandManualResponse>(deactivateResult.Error);
            _manuals.Update(existing);
        }

        var createResult = ClientBrandManual.Create(
            ClientBrandManualId.New(),
            clientId,
            request.OrganizationId,
            request.Title,
            paletteResult.Value,
            logosResult.Value,
            bgResult.Value,
            typoResult.Value,
            request.VoiceAndTone,
            request.Notes,
            _clock.UtcNow);

        if (createResult.IsFailure)
        {
            return Result.Failure<ClientBrandManualResponse>(createResult.Error);
        }

        await _manuals.AddAsync(createResult.Value, cancellationToken);
        await _manuals.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientBrandManualResponse.FromDomain(createResult.Value));
    }
}
