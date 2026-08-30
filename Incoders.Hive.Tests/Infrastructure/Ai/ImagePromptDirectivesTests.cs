using System.Reflection;
using Incoders.Hive.Infrastructure.Ai.Providers;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// Follow-up RED test (task 4.1, closing the WU-4 deferral): asserts both
/// <see cref="OpenAiImageModelClient"/> and <see cref="ComfyUiImageModelClient"/> reference the
/// SAME single defined <see cref="ImagePromptDirectives.BackgroundOnly"/> constant, satisfying
/// CAGH-CUI-01-c literally rather than by coincidence of two independently-maintained copies of
/// identical text. Refs: CAGH-CUI-01-c; design D5.
/// </summary>
public sealed class ImagePromptDirectivesTests
{
    private const string ExpectedDirectiveText =
        "generate ONLY the background; never render text, logos, @handles, or signatures";

    [Fact]
    public void BackgroundOnly_HasExpectedDirectiveText()
    {
        Assert.Equal(ExpectedDirectiveText, ImagePromptDirectives.BackgroundOnly);
    }

    [Fact]
    public void OpenAiImageModelClient_DoesNotDefineItsOwnLocalDirectiveConst()
    {
        // Guards against re-introducing a drift-prone private copy: the only field/const holding
        // the directive text anywhere in the adapter must be the shared one.
        var fields = typeof(OpenAiImageModelClient)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.GetField)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Where(f => (string?)f.GetRawConstantValue() == ExpectedDirectiveText);

        Assert.Empty(fields);
    }

    [Fact]
    public void ComfyUiImageModelClient_DoesNotDefineItsOwnLocalDirectiveConst()
    {
        var fields = typeof(ComfyUiImageModelClient)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.GetField)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Where(f => (string?)f.GetRawConstantValue() == ExpectedDirectiveText);

        Assert.Empty(fields);
    }
}
