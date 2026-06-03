using FluentValidation;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// FluentValidation pipeline validator for <see cref="AnalyzeProfileCommand"/>.
///
/// Validates structural inputs before the MediatR handler executes:
/// - OrganizationId and UserId must be non-empty (always set from the authenticated principal).
/// - Source exclusivity: exactly one of (ClientId+OwnAccountId) or ProfileUrl must be supplied (PA-03).
/// - URL format: when ProfileUrl is supplied, it must be an Instagram profile URL (PA-02).
///
/// Business validations (own-account existence, org membership) remain in
/// <see cref="AnalyzeProfileHandler"/> because they require I/O (database lookups).
///
/// Wired automatically by the <c>ValidationBehavior</c> MediatR pipeline behavior.
/// Registered via <c>AddValidatorsFromAssembly</c> in <c>Application/DependencyInjection.cs</c>.
/// </summary>
public sealed class AnalyzeProfileValidator : AbstractValidator<AnalyzeProfileCommand>
{
    // Instagram profile URL patterns:
    // - https://www.instagram.com/username/
    // - https://instagram.com/username
    // - http variants (less common but valid)
    private static readonly System.Text.RegularExpressions.Regex InstagramProfileUrlPattern =
        new(@"^https?://(www\.)?instagram\.com/[\w._]+/?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public AnalyzeProfileValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("social.profile_analysis.errors.organization_required");

        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("social.profile_analysis.errors.user_required");

        // PA-03: mutual exclusion — both paths supplied
        RuleFor(x => x)
            .Must(x => !(HasClientPath(x) && HasUrlPath(x)))
            .WithMessage("social.profile_analysis.errors.profile_source_mutually_exclusive")
            .OverridePropertyName("Source");

        // PA-03: mutual exclusion — neither path supplied
        RuleFor(x => x)
            .Must(x => HasClientPath(x) || HasUrlPath(x))
            .WithMessage("social.profile_analysis.errors.profile_source_required")
            .OverridePropertyName("Source");

        // PA-02: when URL path is used, the URL must be a valid Instagram profile URL
        When(x => HasUrlPath(x), () =>
        {
            RuleFor(x => x.ProfileUrl!)
                .MaximumLength(2048)
                .WithMessage("social.profile_analysis.errors.profile_url_too_long")
                .Matches(InstagramProfileUrlPattern)
                .WithMessage("social.profile_analysis.errors.profile_url_invalid_instagram");
        });

        // Client path: both ClientId and OwnAccountId required together
        When(HasClientPath, () =>
        {
            RuleFor(x => x.ClientId)
                .NotEmpty()
                .WithMessage("social.profile_analysis.errors.client_id_required");

            RuleFor(x => x.OwnAccountId)
                .NotEmpty()
                .WithMessage("social.profile_analysis.errors.own_account_id_required");
        });
    }

    private static bool HasClientPath(AnalyzeProfileCommand x) =>
        x.ClientId.HasValue && x.OwnAccountId.HasValue;

    private static bool HasUrlPath(AnalyzeProfileCommand x) =>
        !string.IsNullOrWhiteSpace(x.ProfileUrl);
}
