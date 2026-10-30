using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Domain.Social.ContentGeneration;

/// <summary>
/// TDD tests for WU-2: BackgroundSelection.ClientAesthetic variant.
/// Refs: design §3.4; OQ-3/OQ-5.
/// </summary>
public sealed class BackgroundSelectionClientAestheticTests
{
    // -----------------------------------------------------------------------
    // 2.4 RED: BackgroundSelection.ClientAesthetic pattern match
    // -----------------------------------------------------------------------

    [Fact]
    public void ClientAesthetic_WithDescriptors_CanBeConstructed()
    {
        var descriptors = new[] { "minimalist", "dark tones", "urban aesthetic" };

        BackgroundSelection selection = new BackgroundSelection.ClientAesthetic(descriptors);

        var aesthetic = Assert.IsType<BackgroundSelection.ClientAesthetic>(selection);
        Assert.Equal(descriptors, aesthetic.Descriptors);
    }

    [Fact]
    public void ClientAesthetic_PatternMatch_WorksInExhaustiveSwitch()
    {
        var descriptors = new[] { "warm", "earthy" };
        BackgroundSelection selection = new BackgroundSelection.ClientAesthetic(descriptors);

        var label = selection switch
        {
            BackgroundSelection.ForcedDefault => "forced",
            BackgroundSelection.References => "references",
            BackgroundSelection.Creative => "creative",
            BackgroundSelection.ClientAesthetic c => $"aesthetic:{c.Descriptors.Count}",
        };

        Assert.Equal("aesthetic:2", label);
    }

    [Fact]
    public void ClientAesthetic_EmptyDescriptors_DoesNotThrow()
    {
        var descriptors = Array.Empty<string>();

        var selection = new BackgroundSelection.ClientAesthetic(descriptors);

        Assert.Empty(selection.Descriptors);
    }

    [Fact]
    public void ClientAesthetic_IsABackgroundSelection()
    {
        BackgroundSelection selection = new BackgroundSelection.ClientAesthetic(Array.Empty<string>());

        Assert.IsAssignableFrom<BackgroundSelection>(selection);
    }
}
