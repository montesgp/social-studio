using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration;

public sealed class TokenUsageTests
{
    // --- Construction ---

    [Fact]
    public void TokenUsage_WithBothValues_Populated()
    {
        var usage = new TokenUsage(500, 200);

        Assert.Equal(500, usage.InputTokens);
        Assert.Equal(200, usage.OutputTokens);
    }

    [Fact]
    public void TokenUsage_WithNullValues_BothNull()
    {
        var usage = new TokenUsage(null, null);

        Assert.Null(usage.InputTokens);
        Assert.Null(usage.OutputTokens);
    }

    [Fact]
    public void TokenUsage_WithOnlyInput_OutputIsNull()
    {
        var usage = new TokenUsage(100, null);

        Assert.Equal(100, usage.InputTokens);
        Assert.Null(usage.OutputTokens);
    }

    // --- Missing static factory ---

    [Fact]
    public void TokenUsage_Missing_BothFieldsNull()
    {
        var missing = TokenUsage.Missing;

        Assert.Null(missing.InputTokens);
        Assert.Null(missing.OutputTokens);
    }

    // --- Equality ---

    [Fact]
    public void TokenUsage_EqualityByValue_SameValues()
    {
        var a = new TokenUsage(100, 50);
        var b = new TokenUsage(100, 50);

        Assert.Equal(a, b);
    }

    [Fact]
    public void TokenUsage_EqualityByValue_DifferentValues_NotEqual()
    {
        var a = new TokenUsage(100, 50);
        var b = new TokenUsage(200, 50);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TokenUsage_Missing_EqualsNullNullConstructed()
    {
        Assert.Equal(TokenUsage.Missing, new TokenUsage(null, null));
    }

    // --- Nullable integer semantics ---

    [Fact]
    public void TokenUsage_ZeroTokens_AreDistinctFromNull()
    {
        var withZero = new TokenUsage(0, 0);
        var missing = TokenUsage.Missing;

        Assert.NotNull(withZero.InputTokens);
        Assert.NotNull(withZero.OutputTokens);
        Assert.NotEqual(missing, withZero);
    }
}
