using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.ClientBrandManuals;

public sealed record BackgroundEntry(string Name, string Url, string? UseCase)
{
    public const int NameMaxLength = 100;
    public const int UrlMinLength = 10;
    public const int UrlMaxLength = 2048;
    public const int UseCaseMaxLength = 100;

    public static Result<BackgroundEntry> Create(string name, string url, string? useCase)
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

        return Result.Success(new BackgroundEntry(trimmedName, trimmedUrl, trimmedUseCase));
    }

    public static BackgroundEntry Restore(string name, string url, string? useCase)
        => new(name, url, useCase);
}
