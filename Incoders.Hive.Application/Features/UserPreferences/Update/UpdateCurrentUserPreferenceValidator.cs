using FluentValidation;
using Incoders.Hive.Domain.UserPreferences;

namespace Incoders.Hive.Application.Features.UserPreferences.Update;

public sealed class UpdateCurrentUserPreferenceValidator : AbstractValidator<UpdateCurrentUserPreferenceCommand>
{
    public UpdateCurrentUserPreferenceValidator()
    {
        RuleFor(x => x.ThemeKey)
            .NotEmpty()
            .Must(ThemeKeys.IsSupported)
            .WithMessage("ThemeKey must be one of: dark, light, system.");

        RuleFor(x => x.AccentTheme)
            .MaximumLength(64)
            .When(x => !string.IsNullOrWhiteSpace(x.AccentTheme));
    }
}
