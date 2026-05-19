using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Dtos;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals.Create;

public sealed record CreateClientBrandManualCommand(
    Guid OrganizationId,
    Guid ClientId,
    string Title,
    IReadOnlyList<ColorEntryDto> ColorPalette,
    IReadOnlyList<LogoEntryDto> Logos,
    IReadOnlyList<BackgroundEntryDto> BackgroundTemplates,
    IReadOnlyList<TypographyEntryDto> Typography,
    string? VoiceAndTone,
    string? Notes) : IRequest<Result<ClientBrandManualResponse>>;
