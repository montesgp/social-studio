using System.Reflection;
using SocialStudio.Domain.Social.ContentFormats;
using SocialStudio.Domain.Social.ContentStudio;

namespace SocialStudio.Tests.Domain.Social.ContentFormats;

/// <summary>
/// Validates FormatDefinition record structure, IsVisual computed property,
/// and hexagonal purity (SCENARIO-17, S1-T3).
/// </summary>
public sealed class FormatDefinitionTests
{
    private static readonly TargetChannel InstagramStory = new("instagram", "story");

    [Fact]
    public void FormatDefinition_HasExpected_Properties()
    {
        var def = new FormatDefinition(
            FormatKey:     "story",
            Surface:       Surface.Story,
            Layout:        Layout.Single,
            Medium:        Medium.Image,
            Channel:       InstagramStory,
            ContentMedium: ContentMedium.Visual,
            IsMultiAsset:  false,
            DisplayLabel:  "Story",
            StorageFolder: "story");

        Assert.Equal("story", def.FormatKey);
        Assert.Equal(Surface.Story, def.Surface);
        Assert.Equal(Layout.Single, def.Layout);
        Assert.Equal(Medium.Image, def.Medium);
        Assert.Equal(InstagramStory, def.Channel);
        Assert.Equal(ContentMedium.Visual, def.ContentMedium);
        Assert.False(def.IsMultiAsset);
        Assert.Equal("Story", def.DisplayLabel);
        Assert.Equal("story", def.StorageFolder);
    }

    [Fact]
    public void FormatDefinition_Script_CanHaveNullLayout()
    {
        var def = new FormatDefinition(
            FormatKey:     "reel-script",
            Surface:       Surface.Script,
            Layout:        null,
            Medium:        Medium.Text,
            Channel:       new TargetChannel("instagram", "reel"),
            ContentMedium: ContentMedium.Text,
            IsMultiAsset:  false,
            DisplayLabel:  "Script",
            StorageFolder: "script");

        Assert.Null(def.Layout);
        Assert.Equal(Surface.Script, def.Surface);
    }

    [Theory]
    [InlineData(Medium.Image, true)]
    [InlineData(Medium.Video, true)]
    [InlineData(Medium.Text, false)]
    public void FormatDefinition_IsVisual_CorrectForEachMedium(Medium medium, bool expectedIsVisual)
    {
        var def = new FormatDefinition(
            FormatKey:     "test-format",
            Surface:       Surface.Feed,
            Layout:        Layout.Single,
            Medium:        medium,
            Channel:       new TargetChannel("instagram", "feed"),
            ContentMedium: ContentMedium.Visual,
            IsMultiAsset:  false,
            DisplayLabel:  "Test",
            StorageFolder: "test");

        Assert.Equal(expectedIsVisual, def.IsVisual);
    }

    [Fact]
    public void FormatDefinition_IsValueObject_SealedRecord()
    {
        var type = typeof(FormatDefinition);

        Assert.True(type.IsSealed, "FormatDefinition must be sealed");
    }

    [Fact]
    public void FormatDefinition_HasNoForbiddenAttributes_EfOrAspNet()
    {
        // SCENARIO-17: no EF/ASP.NET attributes on domain types.
        var type = typeof(FormatDefinition);
        var forbidden = new[] { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Supabase" };

        var attributeViolations = type
            .GetCustomAttributes(inherit: true)
            .Select(a => a.GetType().Namespace ?? string.Empty)
            .Where(ns => forbidden.Any(f => ns.StartsWith(f, StringComparison.Ordinal)))
            .ToList();

        Assert.True(attributeViolations.Count == 0,
            "FormatDefinition carries forbidden EF/ASP.NET attributes:\n" +
            string.Join("\n", attributeViolations));
    }
}
