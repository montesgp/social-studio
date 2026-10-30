using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.SingleAnalyzer;

public static class SingleAnalyzerPatternErrors
{
    public static readonly Error PatternItemInvalid = Error.Validation(
        "social.single_analyzer.pattern_item_invalid",
        "social.single_analyzer.errors.pattern_item_invalid");

    public static readonly Error PatternSetInvalid = Error.Validation(
        "social.single_analyzer.pattern_set_invalid",
        "social.single_analyzer.errors.pattern_set_invalid");
}
