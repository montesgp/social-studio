using Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.UpdateBackgroundPattern;

namespace Incoders.Hive.Tests.Application.Social.ClientAestheticProfileAdmin;

public sealed class UpdateClientAestheticProfileBackgroundPatternValidatorTests
{
    private readonly UpdateClientAestheticProfileBackgroundPatternValidator _validator = new();

    [Fact]
    public void Valid_PhotoAndFlatColor_Passes()
    {
        var result = _validator.Validate(new UpdateClientAestheticProfileBackgroundPatternCommand(
            Guid.NewGuid(), Guid.NewGuid(), ["Photo", "FlatColor", "Decorator"]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyList_Fails()
    {
        var result = _validator.Validate(new UpdateClientAestheticProfileBackgroundPatternCommand(
            Guid.NewGuid(), Guid.NewGuid(), []));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "social.profile_analysis.errors.background_pattern_empty");
    }

    [Fact]
    public void InvalidValue_Fails()
    {
        var result = _validator.Validate(new UpdateClientAestheticProfileBackgroundPatternCommand(
            Guid.NewGuid(), Guid.NewGuid(), ["Photo", "NotAKind"]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "social.profile_analysis.errors.invalid_background_pattern_value");
    }

    [Fact]
    public void CaseInsensitive_Passes()
    {
        var result = _validator.Validate(new UpdateClientAestheticProfileBackgroundPatternCommand(
            Guid.NewGuid(), Guid.NewGuid(), ["photo", "flatcolor"]));

        Assert.True(result.IsValid);
    }
}
