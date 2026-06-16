using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Tests.Domain.Social.ContentStudio;

public sealed class ContentOriginTests
{
    [Fact]
    public void GeneratorKind_SingleAnalyzer_IsZero()
    {
        Assert.Equal(0, (int)GeneratorKind.SingleAnalyzer);
    }

    [Fact]
    public void ContentOrigin_HoldsAllFields_AndHasValueEquality()
    {
        var runId = Guid.NewGuid();
        var resultId = Guid.NewGuid();

        var a = new ContentOrigin(GeneratorKind.SingleAnalyzer, runId, null, resultId, "story");
        var b = new ContentOrigin(GeneratorKind.SingleAnalyzer, runId, null, resultId, "story");

        Assert.Equal(GeneratorKind.SingleAnalyzer, a.GeneratorKind);
        Assert.Equal(runId, a.SourceRunId);
        Assert.Null(a.SourcePackageId);
        Assert.Equal(resultId, a.FormatResultId);
        Assert.Equal("story", a.FormatKey);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ContentOrigin_SupportsAFutureGeneratorKind_WithoutStructuralChange()
    {
        var origin = new ContentOrigin(
            GeneratorKind.MagicGenerator, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "carousel");

        Assert.Equal(GeneratorKind.MagicGenerator, origin.GeneratorKind);
    }
}
