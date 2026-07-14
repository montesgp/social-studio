using FluentValidation;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.UpdateBackgroundPattern;

public sealed class UpdateClientAestheticProfileBackgroundPatternValidator
    : AbstractValidator<UpdateClientAestheticProfileBackgroundPatternCommand>
{
    private const int MaxSlides = 50;

    public UpdateClientAestheticProfileBackgroundPatternValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.profile_analysis.errors.organization_required");

        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.profile_analysis.errors.client_required");

        RuleFor(x => x.BackgroundPattern)
            .NotNull()
            .Must(p => p is { Count: > 0 })
                .WithMessage("social.profile_analysis.errors.background_pattern_empty")
            .Must(p => p is null || p.Count <= MaxSlides)
                .WithMessage("social.profile_analysis.errors.background_pattern_too_long")
            .Must(p => p is null || p.All(IsValidSlideBackgroundKind))
                .WithMessage("social.profile_analysis.errors.invalid_background_pattern_value");
    }

    private static bool IsValidSlideBackgroundKind(string value) =>
        Enum.TryParse<SlideBackgroundKind>(value, ignoreCase: true, out _);
}
