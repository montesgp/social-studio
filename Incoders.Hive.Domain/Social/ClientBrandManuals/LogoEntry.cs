using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientBrandManuals;

public sealed record LogoEntry(string Name, string Url, string? Variant)
{
    public const int NameMaxLength = 100;
    public const int UrlMinLength = 10;
    public const int UrlMaxLength = 2048;
    public const int VariantMaxLength = 50;

    public static Result<LogoEntry> Create(string name, string url, string? variant)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<LogoEntry>(ClientBrandManualErrors.Validation("Name is required."));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
        {
            return Result.Failure<LogoEntry>(ClientBrandManualErrors.Validation(
                $"Name must not exceed {NameMaxLength} characters."));
        }

        var urlError = ValidateUrl(url, "Url");
        if (urlError is not null)
        {
            return Result.Failure<LogoEntry>(urlError);
        }

        var trimmedUrl = url.Trim();

        string? trimmedVariant = null;
        if (variant is not null)
        {
            trimmedVariant = variant.Trim();
            if (trimmedVariant.Length == 0)
            {
                trimmedVariant = null;
            }
            else if (trimmedVariant.Length > VariantMaxLength)
            {
                return Result.Failure<LogoEntry>(ClientBrandManualErrors.Validation(
                    $"Variant must not exceed {VariantMaxLength} characters."));
            }
        }

        return Result.Success(new LogoEntry(trimmedName, trimmedUrl, trimmedVariant));
    }

    public static LogoEntry Restore(string name, string url, string? variant)
        => new(name, url, variant);

    public static Error? ValidateUrl(string url, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return ClientBrandManualErrors.Validation($"{fieldName} is required.");
        }

        var trimmed = url.Trim();
        if (trimmed.Length < UrlMinLength)
        {
            return ClientBrandManualErrors.Validation(
                $"{fieldName} must be at least {UrlMinLength} characters.");
        }

        if (trimmed.Length > UrlMaxLength)
        {
            return ClientBrandManualErrors.Validation(
                $"{fieldName} must not exceed {UrlMaxLength} characters.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ClientBrandManualErrors.Validation(
                $"{fieldName} must be an absolute http or https URL.");
        }

        return null;
    }
}
