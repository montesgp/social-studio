using FluentValidation;
using SocialStudio.Domain.Social.SingleAnalyzer;

namespace SocialStudio.Application.Features.Social.SingleAnalyzer.Analyze;

public sealed class AnalyzeSingleContentValidator : AbstractValidator<AnalyzeSingleContentCommand>
{
    public AnalyzeSingleContentValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage(SingleAnalyzerErrors.OrganizationRequired.MessageKey);

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage(SingleAnalyzerErrors.UserRequired.MessageKey);

        RuleFor(x => x.Url)
            .NotEmpty().WithMessage(SingleAnalyzerErrors.UrlRequired.MessageKey)
            .MaximumLength(SingleAnalyzerSourceUrl.MaxLength).WithMessage(SingleAnalyzerErrors.UrlInvalid.MessageKey);
    }
}
