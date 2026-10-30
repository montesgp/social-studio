using SocialStudio.Domain.Social.ContentStudio;

namespace SocialStudio.Tests.Domain.Social.ContentStudio;

/// <summary>
/// TDD tests for <see cref="Caption"/> — Decision E1/Q3 (Content Studio editing completeness).
/// Instagram's concrete publishing limits encoded as domain invariants: 2,200 characters, 30
/// hashtags.
/// </summary>
public sealed class CaptionTests
{
    [Fact]
    public void Create_ValidCaption_Succeeds()
    {
        var result = Caption.Create("Grow your brand today. #growth #brand");

        Assert.True(result.IsSuccess);
        Assert.Equal("Grow your brand today. #growth #brand", result.Value.Value);
    }

    [Fact]
    public void Create_Null_Fails()
    {
        var result = Caption.Create(null);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_Empty_Fails()
    {
        var result = Caption.Create(string.Empty);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_WhitespaceOnly_Fails()
    {
        var result = Caption.Create("   \n\t  ");

        Assert.True(result.IsFailure);
    }

    // -----------------------------------------------------------------------
    // Length boundary: 2,200 characters (Instagram's publishing limit)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_ExactlyMaxLength_2200_Succeeds()
    {
        var text = new string('a', 2200);

        var result = Caption.Create(text);

        Assert.True(result.IsSuccess);
        Assert.Equal(2200, result.Value.Value.Length);
    }

    [Fact]
    public void Create_JustUnderMaxLength_2199_Succeeds()
    {
        var text = new string('a', 2199);

        var result = Caption.Create(text);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_OverMaxLength_2201_Fails()
    {
        var text = new string('a', 2201);

        var result = Caption.Create(text);

        Assert.True(result.IsFailure);
    }

    // -----------------------------------------------------------------------
    // Hashtag boundary: 30 hashtags (Instagram's publishing limit)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_Exactly30Hashtags_Succeeds()
    {
        var text = string.Join(' ', Enumerable.Range(1, 30).Select(i => $"#tag{i}"));

        var result = Caption.Create(text);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_29Hashtags_Succeeds()
    {
        var text = string.Join(' ', Enumerable.Range(1, 29).Select(i => $"#tag{i}"));

        var result = Caption.Create(text);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_31Hashtags_Fails()
    {
        var text = string.Join(' ', Enumerable.Range(1, 31).Select(i => $"#tag{i}"));

        var result = Caption.Create(text);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_TrimsSurroundingWhitespace()
    {
        var result = Caption.Create("  Hello world  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello world", result.Value.Value);
    }
}
