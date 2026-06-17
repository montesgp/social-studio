using Incoders.Hive.Infrastructure.Persistence.Postgres;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// Guards the persistence-boundary truncation that prevents Postgres
/// <c>22001: value too long for type character varying(n)</c> from aborting a save and
/// leaving the job stuck in "Running" (regression: story/carousel jobs never reached a
/// terminal status because the failure-reason write itself overflowed error_code(128)).
/// </summary>
public sealed class StringColumnTests
{
    [Fact]
    public void Truncate_ValueWithinLimit_ReturnedUnchanged()
    {
        var value = new string('a', 50);

        var result = StringColumn.Truncate(value, 128);

        Assert.Equal(value, result);
    }

    [Fact]
    public void Truncate_ValueAtExactLimit_ReturnedUnchanged()
    {
        var value = new string('a', 128);

        var result = StringColumn.Truncate(value, 128);

        Assert.Equal(value, result);
    }

    [Fact]
    public void Truncate_OverLengthValue_ClampedToMaxLength()
    {
        // The real bug: a ~195-char "InvalidOperationException: ProviderCallContext.Current is
        // null…" message was written into error_code(128). After truncation it MUST fit.
        var value = new string('x', 300);

        var result = StringColumn.Truncate(value, 128);

        Assert.NotNull(result);
        Assert.Equal(128, result!.Length);
    }

    [Fact]
    public void Truncate_OverLengthValue_EndsWithEllipsisMarker()
    {
        var value = new string('x', 300);

        var result = StringColumn.Truncate(value, 128);

        Assert.EndsWith("…", result);
    }

    [Fact]
    public void Truncate_Null_ReturnsNull()
    {
        Assert.Null(StringColumn.Truncate(null, 128));
    }
}
