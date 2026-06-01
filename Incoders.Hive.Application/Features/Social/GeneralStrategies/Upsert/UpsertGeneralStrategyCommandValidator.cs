using FluentValidation;
using Incoders.Hive.Domain.Social.GeneralStrategies;

namespace Incoders.Hive.Application.Features.Social.GeneralStrategies.Upsert;

public sealed class UpsertGeneralStrategyCommandValidator : AbstractValidator<UpsertGeneralStrategyCommand>
{
    public UpsertGeneralStrategyCommandValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.general_strategies.errors.client_id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.general_strategies.errors.organization_required");

        RuleFor(x => x.ContentPillars)
            .Must(p => p is null || p.Count <= GeneralStrategy.ContentPillarsMaxCount)
            .WithMessage($"social.general_strategies.errors.content_pillars_too_many");

        RuleFor(x => x.ToneKeywords)
            .Must(k => k is null || k.Count <= GeneralStrategy.ToneKeywordsMaxCount)
            .WithMessage($"social.general_strategies.errors.tone_keywords_too_many");

        RuleFor(x => x.TargetAudience)
            .MaximumLength(GeneralStrategy.TargetAudienceMaxLength)
            .WithMessage("social.general_strategies.errors.target_audience_too_long");

        RuleFor(x => x.PostingFrequency)
            .MaximumLength(GeneralStrategy.PostingFrequencyMaxLength)
            .WithMessage("social.general_strategies.errors.posting_frequency_too_long");

        RuleFor(x => x.Notes)
            .MaximumLength(GeneralStrategy.NotesMaxLength)
            .WithMessage("social.general_strategies.errors.notes_too_long");
    }
}
