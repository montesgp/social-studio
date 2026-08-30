using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Dtos;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;

namespace Incoders.Hive.Application.Features.Social.ClientBrandManuals.Converters;

/// <summary>
/// Converts Application-layer DTOs to validated Domain value-object records.
/// Each method returns Result failure on first invalid entry.
/// </summary>
internal static class BrandManualDtoConverters
{
    public static Result<List<ColorEntry>> ToColorEntries(IReadOnlyList<ColorEntryDto> dtos)
    {
        var list = new List<ColorEntry>(dtos.Count);
        foreach (var dto in dtos)
        {
            var r = ColorEntry.Create(dto.Name, dto.Hex, dto.Role, dto.Rank);
            if (r.IsFailure) return Result.Failure<List<ColorEntry>>(r.Error);
            list.Add(r.Value);
        }
        return Result.Success(list);
    }

    public static Result<List<LogoEntry>> ToLogoEntries(IReadOnlyList<LogoEntryDto> dtos)
    {
        var list = new List<LogoEntry>(dtos.Count);
        foreach (var dto in dtos)
        {
            var r = LogoEntry.Create(dto.Name, dto.Url, dto.Variant);
            if (r.IsFailure) return Result.Failure<List<LogoEntry>>(r.Error);
            list.Add(r.Value);
        }
        return Result.Success(list);
    }

    public static Result<List<BackgroundEntry>> ToBackgroundEntries(IReadOnlyList<BackgroundEntryDto> dtos)
    {
        var list = new List<BackgroundEntry>(dtos.Count);
        foreach (var dto in dtos)
        {
            var r = BackgroundEntry.Create(dto.Name, dto.Url, dto.UseCase);
            if (r.IsFailure) return Result.Failure<List<BackgroundEntry>>(r.Error);
            list.Add(r.Value);
        }
        return Result.Success(list);
    }

    public static Result<List<TypographyEntry>> ToTypographyEntries(IReadOnlyList<TypographyEntryDto> dtos)
    {
        var list = new List<TypographyEntry>(dtos.Count);
        foreach (var dto in dtos)
        {
            var r = TypographyEntry.Create(dto.Name, dto.FontFamily, dto.Role, dto.FontUrl, dto.Weight);
            if (r.IsFailure) return Result.Failure<List<TypographyEntry>>(r.Error);
            list.Add(r.Value);
        }
        return Result.Success(list);
    }
}
