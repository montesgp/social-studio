using Incoders.Hive.Domain.Social.ClientBrandManuals;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals;

public sealed record ClientBrandManualResponse(
    Guid Id,
    Guid ClientId,
    Guid OrganizationId,
    string Title,
    IReadOnlyList<ColorEntry> ColorPalette,
    IReadOnlyList<LogoEntry> Logos,
    IReadOnlyList<BackgroundEntry> BackgroundTemplates,
    IReadOnlyList<TypographyEntry> Typography,
    string? VoiceAndTone,
    string? Notes,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static ClientBrandManualResponse FromDomain(ClientBrandManual m) => new(
        m.Id.Value,
        m.ClientId.Value,
        m.OrganizationId,
        m.Title,
        m.ColorPalette,
        m.Logos,
        m.BackgroundTemplates,
        m.Typography,
        m.VoiceAndTone,
        m.Notes,
        m.IsActive,
        m.CreatedAtUtc,
        m.UpdatedAtUtc);
}

public sealed record PagedClientBrandManualsResponse(
    IReadOnlyList<ClientBrandManualResponse> Items,
    int TotalCount);
