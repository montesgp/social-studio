using FluentValidation;
using Incoders.Hive.Domain.Social.ClientStrategies;

namespace Incoders.Hive.Application.Features.Social.ClientStrategies.Upsert;

public sealed class UpsertClientStrategyCommandValidator : AbstractValidator<UpsertClientStrategyCommand>
{
    public UpsertClientStrategyCommandValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("social.client_strategies.errors.client_id_required");

        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("social.client_strategies.errors.organization_required");

        RuleFor(x => x.ContentPillars)
            .Must(p => p is null || p.Count <= ClientStrategy.ContentPillarsMaxCount)
            .WithMessage($"social.client_strategies.errors.content_pillars_too_many");

        RuleFor(x => x.ToneKeywords)
            .Must(k => k is null || k.Count <= ClientStrategy.ToneKeywordsMaxCount)
            .WithMessage($"social.client_strategies.errors.tone_keywords_too_many");

        RuleFor(x => x.TargetAudience)
            .MaximumLength(ClientStrategy.TargetAudienceMaxLength)
            .WithMessage("social.client_strategies.errors.target_audience_too_long");

        RuleFor(x => x.PostingFrequency)
            .MaximumLength(ClientStrategy.PostingFrequencyMaxLength)
            .WithMessage("social.client_strategies.errors.posting_frequency_too_long");

        RuleFor(x => x.Notes)
            .MaximumLength(ClientStrategy.NotesMaxLength)
            .WithMessage("social.client_strategies.errors.notes_too_long");
    }
}
