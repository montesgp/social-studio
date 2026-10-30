using SocialStudio.Application.Features.Social.ContentStudio.Materialize;

namespace SocialStudio.Tests.Application.Social.ContentStudio;

/// <summary>
/// Tests for <see cref="VisualPublicationCaption"/> — the in-call caption extractor that lets a
/// materialized visual post carry the description + hashtags the AI produced in the same generation call.
/// </summary>
public sealed class VisualPublicationCaptionTests
{
    [Fact]
    public void Extract_DescriptionAndHashtags_JoinsBoth()
    {
        var json = """{"coverTitle":"X","publicationDescription":"Grow your brand today.","hashtags":["#growth","#brand"]}""";

        var caption = VisualPublicationCaption.Extract(json);

        Assert.Equal("Grow your brand today.\n\n#growth #brand", caption);
    }

    [Fact]
    public void Extract_DescriptionOnly_ReturnsDescription()
    {
        var json = """{"publicationDescription":"Just the copy.","hashtags":[]}""";

        Assert.Equal("Just the copy.", VisualPublicationCaption.Extract(json));
    }

    [Fact]
    public void Extract_HashtagsOnly_ReturnsHashtags()
    {
        var json = """{"publicationDescription":null,"hashtags":["#a","#b"]}""";

        Assert.Equal("#a #b", VisualPublicationCaption.Extract(json));
    }

    [Fact]
    public void Extract_TrimsAndSkipsBlankHashtags()
    {
        var json = """{"publicationDescription":"  Copy.  ","hashtags":["  #a  ","",null,"#b"]}""";

        Assert.Equal("Copy.\n\n#a #b", VisualPublicationCaption.Extract(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"coverTitle\":\"no caption fields\"}")]
    [InlineData("{\"publicationDescription\":\"\",\"hashtags\":[]}")]
    public void Extract_AbsentMalformedOrEmpty_ReturnsNull(string? json)
    {
        Assert.Null(VisualPublicationCaption.Extract(json));
    }
}
