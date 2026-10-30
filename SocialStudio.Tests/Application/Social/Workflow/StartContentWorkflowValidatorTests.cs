using SocialStudio.Application.Features.Social.Workflow;

namespace SocialStudio.Tests.Application.Social.Workflow;

/// <summary>
/// Covers the <c>MaxResults</c> validation bound added alongside the discovery cap fix: mirrors the
/// Generator UI's own <c>min=1 / max=100</c> constraint on the "Max results" input.
/// </summary>
public sealed class StartContentWorkflowValidatorTests
{
    private static StartContentWorkflowCommand ValidCommand(int? maxResults) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"], MaxResults: maxResults);

    [Fact]
    public void MaxResults_Omitted_IsValid()
    {
        var result = new StartContentWorkflowValidator().Validate(ValidCommand(null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void MaxResults_WithinBounds_IsValid(int maxResults)
    {
        var result = new StartContentWorkflowValidator().Validate(ValidCommand(maxResults));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void MaxResults_OutOfBounds_IsInvalid(int maxResults)
    {
        var result = new StartContentWorkflowValidator().Validate(ValidCommand(maxResults));

        Assert.False(result.IsValid);
    }
}
