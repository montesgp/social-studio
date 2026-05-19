using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientBrandManuals;

public sealed record TypographyEntry(string Name, string FontFamily, string? Role, string? FontUrl, string? Weight)
{
    public const int NameMaxLength = 100;
    public const int FontFamilyMaxLength = 100;
    public const int RoleMaxLength = 50;
    public const int FontUrlMinLength = 10;
    public const int FontUrlMaxLength = 2048;
    public const int WeightMaxLength = 50;

    public static Result<TypographyEntry> Create(
        string name,
        string fontFamily,
        string? role,
        string? fontUrl,
        string? weight)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<TypographyEntry>(ClientBrandManualErrors.Validation("Name is required."));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
        {
            return Result.Failure<TypographyEntry>(ClientBrandManualErrors.Validation(
                $"Name must not exceed {NameMaxLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return Result.Failure<TypographyEntry>(ClientBrandManualErrors.Validation("FontFamily is required."));
        }

        var trimmedFontFamily = fontFamily.Trim();
        if (trimmedFontFamily.Length > FontFamilyMaxLength)
        {
            return Result.Failure<TypographyEntry>(ClientBrandManualErrors.Validation(
                $"FontFamily must not exceed {FontFamilyMaxLength} characters."));
        }

        string? trimmedRole = null;
        if (role is not null)
        {
            trimmedRole = role.Trim();
            if (trimmedRole.Length == 0)
            {
                trimmedRole = null;
            }
            else if (trimmedRole.Length > RoleMaxLength)
            {
                return Result.Failure<TypographyEntry>(ClientBrandManualErrors.Validation(
                    $"Role must not exceed {RoleMaxLength} characters."));
            }
        }

        string? trimmedFontUrl = null;
        if (fontUrl is not null)
        {
            var fontUrlTrimmed = fontUrl.Trim();
            if (fontUrlTrimmed.Length > 0)
            {
                if (fontUrlTrimmed.Length < FontUrlMinLength)
                {
                    return Result.Failure<TypographyEntry>(ClientBrandManualErrors.Validation(
                        $"FontUrl must be at least {FontUrlMinLength} characters."));
                }

                if (fontUrlTrimmed.Length > FontUrlMaxLength)
                {
                    return Result.Failure<TypographyEntry>(ClientBrandManualErrors.Validation(
                        $"FontUrl must not exceed {FontUrlMaxLength} characters."));
                }

                if (!Uri.TryCreate(fontUrlTrimmed, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return Result.Failure<TypographyEntry>(ClientBrandManualErrors.Validation(
                        "FontUrl must be an absolute http or https URL."));
                }

                trimmedFontUrl = fontUrlTrimmed;
            }
        }

        string? trimmedWeight = null;
        if (weight is not null)
        {
            trimmedWeight = weight.Trim();
            if (trimmedWeight.Length == 0)
            {
                trimmedWeight = null;
            }
            else if (trimmedWeight.Length > WeightMaxLength)
            {
                return Result.Failure<TypographyEntry>(ClientBrandManualErrors.Validation(
                    $"Weight must not exceed {WeightMaxLength} characters."));
            }
        }

        return Result.Success(new TypographyEntry(
            trimmedName, trimmedFontFamily, trimmedRole, trimmedFontUrl, trimmedWeight));
    }

    public static TypographyEntry Restore(
        string name,
        string fontFamily,
        string? role,
        string? fontUrl,
        string? weight)
        => new(name, fontFamily, role, fontUrl, weight);
}
