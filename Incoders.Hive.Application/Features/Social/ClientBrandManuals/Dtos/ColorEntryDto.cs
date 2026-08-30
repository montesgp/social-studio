using Incoders.Hive.Domain.Social.ClientBrandManuals;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals.Dtos;

/// <summary>
/// Application-layer DTO for a brand color entry.
/// Role is accepted as a structured enum; Rank defaults to 0 when not provided by the caller.
/// </summary>
public sealed record ColorEntryDto(string Name, string Hex, ColorRole? Role, int Rank = 0);
