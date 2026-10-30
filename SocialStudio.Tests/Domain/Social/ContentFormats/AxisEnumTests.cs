using System.Reflection;
using SocialStudio.Domain.Social.ContentFormats;

namespace SocialStudio.Tests.Domain.Social.ContentFormats;

/// <summary>
/// Validates the three axis enum types (Surface, Layout, Medium) for correct membership,
/// extensibility intent, and hexagonal purity (SCENARIO-17, S1-T1).
/// </summary>
public sealed class AxisEnumTests
{
    [Fact]
    public void Surface_HasExactly_Script_Story_Feed()
    {
        var values = Enum.GetValues<Surface>();

        Assert.Equal(3, values.Length);
        Assert.Contains(Surface.Script, values);
        Assert.Contains(Surface.Story, values);
        Assert.Contains(Surface.Feed, values);
    }

    [Fact]
    public void Layout_HasExactly_Single_Carousel()
    {
        var values = Enum.GetValues<Layout>();

        Assert.Equal(2, values.Length);
        Assert.Contains(Layout.Single, values);
        Assert.Contains(Layout.Carousel, values);
    }

    [Fact]
    public void Medium_HasExactly_Image_Video_Text()
    {
        var values = Enum.GetValues<Medium>();

        Assert.Equal(3, values.Length);
        Assert.Contains(Medium.Image, values);
        Assert.Contains(Medium.Video, values);
        Assert.Contains(Medium.Text, values);
    }

    [Fact]
    public void Medium_Video_IsRepresentable_AsValidEnumValue()
    {
        // Extensibility proof: Medium.Video must be a real enum member even though
        // no video generator ships in v1 (REQ-10 / SCENARIO-16 precondition).
        var defined = Enum.IsDefined(typeof(Medium), Medium.Video);

        Assert.True(defined);
    }

    [Fact]
    public void AxisEnums_HaveNoDependency_OnEfOrAspNetTypes()
    {
        // SCENARIO-17: domain types must not reference EF Core / ASP.NET / Supabase.
        var domainAssembly = typeof(Surface).Assembly;
        var forbidden = new[] { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Supabase" };

        var violations = domainAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("SocialStudio.Domain.Social.ContentFormats",
                            StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetCustomAttributes(inherit: false))
            .Select(a => a.GetType().Namespace ?? string.Empty)
            .Where(ns => forbidden.Any(f => ns.StartsWith(f, StringComparison.Ordinal)))
            .ToList();

        Assert.True(violations.Count == 0,
            "ContentFormats domain types carry forbidden EF/ASP.NET attributes:\n" +
            string.Join("\n", violations));
    }
}
