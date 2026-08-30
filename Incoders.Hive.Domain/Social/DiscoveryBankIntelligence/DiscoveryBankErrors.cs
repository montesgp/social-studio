using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.DiscoveryBankIntelligence;

/// <summary>
/// Errors of the AI Analysis tool, which interrogates the discovery candidate bank.
///
/// <see cref="UnsupportedQuestion"/> and <see cref="InvalidQueryParameter"/> are deliberately
/// VALIDATION errors rather than failures: they mean the model's plan was rejected before anything ran,
/// which is the tool working as designed, not an outage.
/// </summary>
public static class DiscoveryBankErrors
{
    public static readonly Error OrganizationRequired = Error.Validation(
        "social.discovery_bank.organization_required",
        "social.discovery_bank.errors.organization_required");

    public static readonly Error QuestionRequired = Error.Validation(
        "social.discovery_bank.question_required",
        "social.discovery_bank.errors.question_required");

    public static readonly Error QuestionTooLong = Error.Validation(
        "social.discovery_bank.question_too_long",
        "social.discovery_bank.errors.question_too_long");

    /// <summary>The question does not map onto any query in the enumerated catalog.</summary>
    public static readonly Error UnsupportedQuestion = Error.Validation(
        "social.discovery_bank.unsupported_question",
        "social.discovery_bank.errors.unsupported_question");

    /// <summary>The plan named a supported query but a parameter fell outside its allowed domain.</summary>
    public static readonly Error InvalidQueryParameter = Error.Validation(
        "social.discovery_bank.invalid_query_parameter",
        "social.discovery_bank.errors.invalid_query_parameter");

    /// <summary>The narration call failed after the query had already been computed.</summary>
    public static readonly Error NarrationFailed = Error.Failure(
        "social.discovery_bank.narration_failed",
        "social.discovery_bank.errors.narration_failed");
}
