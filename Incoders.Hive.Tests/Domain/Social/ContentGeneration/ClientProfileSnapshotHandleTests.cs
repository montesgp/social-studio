using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration;

/// <summary>
/// RED tests for WU-5, task 5.1:
/// Asserts that ClientProfileSnapshot.Handle is an additive nullable string field.
/// Existing construction without the Handle param must compile unchanged.
/// with { Handle = "@brand" } must compile and round-trip the value.
/// </summary>
public sealed class ClientProfileSnapshotHandleTests
{
    [Fact]
    public void Handle_DefaultsToNull_WhenConstructedWithoutHandleParam()
    {
        // Existing callers omit Handle — must still compile and produce null.
        var snapshot = new ClientProfileSnapshot("Acme Corp", null, null);

        Assert.Null(snapshot.Handle);
    }

    [Fact]
    public void Handle_RoundTrips_WhenSetViaWithExpression()
    {
        var snapshot = new ClientProfileSnapshot("Acme Corp", null, null);

        var withHandle = snapshot with { Handle = "@brand" };

        Assert.Equal("@brand", withHandle.Handle);
    }

    [Fact]
    public void Handle_OriginalSnapshotUnchanged_AfterWithExpression()
    {
        // Records are immutable — with { } does not mutate the original.
        var snapshot = new ClientProfileSnapshot("Acme Corp", null, null);
        _ = snapshot with { Handle = "@brand" };

        Assert.Null(snapshot.Handle);
    }

    [Fact]
    public void Handle_IsString_WhenPassedDirectlyToConstructor()
    {
        var snapshot = new ClientProfileSnapshot("Brand X", null, null, Handle: "@explicit");

        Assert.Equal("@explicit", snapshot.Handle);
    }
}
