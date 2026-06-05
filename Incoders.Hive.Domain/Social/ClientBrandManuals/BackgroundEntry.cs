using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientBrandManuals;

public sealed record BackgroundEntry(
    string Name,
    string Url,
    string? UseCase,
    BackgroundTier Tier = BackgroundTier.Creative,
    string? FormatKey = null,
    bool IsFormatDefault = false)
{
    public const int NameMaxLength = 100;
    public const int UrlMinLength = 10;
    public const int UrlMaxLength = 2048;
    public const int UseCaseMaxLength = 100;
    public const int FormatKeyMaxLength = 100;

    public static Result<BackgroundEntry> Create(
        string name,
        string url,
        string? useCase,
        BackgroundTier tier = BackgroundTier.Creative,
        string? formatKey = null,
        bool isFormatDefault = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<BackgroundEntry>(ClientBrandManualErrors.Validation("Name is required."));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
        {
            return Result.Failure<BackgroundEntry>(ClientBrandManualErrors.Validation(
                $"Name must not exceed {NameMaxLength} characters."));
        }

        var urlError = LogoEntry.ValidateUrl(url, "Url");
        if (urlError is not null)
        {
            return Result.Failure<BackgroundEntry>(urlError);
        }

        var trimmedUrl = url.Trim();

        string? trimmedUseCase = null;
        if (useCase is not null)
        {
            trimmedUseCase = useCase.Trim();
            if (trimmedUseCase.Length == 0)
            {
                trimmedUseCase = null;
            }
            else if (trimmedUseCase.Length > UseCaseMaxLength)
            {
                return Result.Failure<BackgroundEntry>(ClientBrandManualErrors.Validation(
                    $"UseCase must not exceed {UseCaseMaxLength} characters."));
            }
        }

        // Normalize FormatKey (trim, null-if-empty) like UseCase.
        string? trimmedFormatKey = null;
        if (formatKey is not null)
        {
            trimmedFormatKey = formatKey.Trim();
            if (trimmedFormatKey.Length == 0)
            {
                trimmedFormatKey = null;
            }
            else if (trimmedFormatKey.Length > FormatKeyMaxLength)
            {
                return Result.Failure<BackgroundEntry>(ClientBrandManualErrors.Validation(
                    $"FormatKey must not exceed {FormatKeyMaxLength} characters."));
            }
        }

        // Entry-level guard: IsFormatDefault=true requires non-empty FormatKey AND ForcedDefault tier.
        if (isFormatDefault)
        {
            if (string.IsNullOrWhiteSpace(trimmedFormatKey))
            {
                return Result.Failure<BackgroundEntry>(ClientBrandManualErrors.Validation(
                    "A forced default entry requires a non-empty FormatKey."));
            }

            if (tier != BackgroundTier.ForcedDefault)
            {
                return Result.Failure<BackgroundEntry>(ClientBrandManualErrors.Validation(
                    "A forced default entry must use BackgroundTier.ForcedDefault."));
            }
        }

        return Result.Success(new BackgroundEntry(trimmedName, trimmedUrl, trimmedUseCase,
            tier, trimmedFormatKey, isFormatDefault));
    }

    public static BackgroundEntry Restore(
        string name,
        string url,
        string? useCase,
        BackgroundTier tier = BackgroundTier.Creative,
        string? formatKey = null,
        bool isFormatDefault = false)
        => new(name, url, useCase, tier, formatKey, isFormatDefault);
}
