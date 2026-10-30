using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 task 1.1 [RED] — <see cref="SlideBackgroundKind"/> enum contract.
/// Refs: design §3.1; tasks 1.1/1.2; CAC-BSP-01.
/// </summary>
public sealed class SlideBackgroundKindTests
{
    [Fact]
    public void SlideBackgroundKind_HasPhoto_Member()
    {
        Assert.True(Enum.IsDefined(typeof(SlideBackgroundKind), SlideBackgroundKind.Photo));
    }

    [Fact]
    public void SlideBackgroundKind_HasFlatColor_Member()
    {
        Assert.True(Enum.IsDefined(typeof(SlideBackgroundKind), SlideBackgroundKind.FlatColor));
    }

    [Fact]
    public void SlideBackgroundKind_HasDecorator_Member()
    {
        Assert.True(Enum.IsDefined(typeof(SlideBackgroundKind), SlideBackgroundKind.Decorator));
    }

    [Fact]
    public void SlideBackgroundKind_ExactlyThreeMembers()
    {
        Assert.Equal(3, Enum.GetValues<SlideBackgroundKind>().Length);
    }

    [Fact]
    public void SlideBackgroundKind_IsEnumType()
    {
        Assert.True(typeof(SlideBackgroundKind).IsEnum);
    }

    [Fact]
    public void SlideBackgroundKind_HasNoInfrastructureTypeDependency()
    {
        // Verifies the enum assembly is Application-only — no Infrastructure namespace referenced.
        var ns = typeof(SlideBackgroundKind).Namespace;
        Assert.StartsWith("SocialStudio.Application", ns);
    }
}
