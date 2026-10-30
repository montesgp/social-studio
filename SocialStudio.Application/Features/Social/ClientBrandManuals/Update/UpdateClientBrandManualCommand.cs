using SocialStudio.Application.Features.Social.ClientBrandManuals.Dtos;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ClientBrandManuals.Update;

public sealed record UpdateClientBrandManualCommand(
    Guid OrganizationId,
    Guid ClientId,
    Guid Id,
    string Title,
    IReadOnlyList<ColorEntryDto> ColorPalette,
    IReadOnlyList<LogoEntryDto> Logos,
    IReadOnlyList<BackgroundEntryDto> BackgroundTemplates,
    IReadOnlyList<TypographyEntryDto> Typography,
    string? VoiceAndTone,
    string? Notes) : IRequest<Result<ClientBrandManualResponse>>;
