using Incoders.Hive.Domain.Social.ContentPlans;

namespace Incoders.Hive.Tests.Domain.Social;

public sealed class ProvenanceTests
{
    private static readonly DateTime NowUtc = new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    // --- CP-02: Empty factory ---

    [Fact]
    public void Empty_HasEmptyStatus_AndManualSource()
    {
        var p = Provenance.Empty;

        Assert.Equal(ProvenanceSource.Manual, p.Source);
        Assert.Equal(ProvenanceStatus.Empty, p.Status);
        Assert.Null(p.GeneratedAtUtc);
        Assert.Null(p.PromptVersion);
    }

    [Fact]
    public void Empty_IsIdempotent()
    {
        var p1 = Provenance.Empty;
        var p2 = Provenance.Empty;

        Assert.Equal(p1, p2);
    }

    // --- CP-02: Manual factory (Status=Draft) ---

    [Fact]
    public void Manual_HasDraftStatus_AndManualSource()
    {
        var p = Provenance.Manual(NowUtc);

        Assert.Equal(ProvenanceSource.Manual, p.Source);
        Assert.Equal(ProvenanceStatus.Draft, p.Status);
        Assert.Null(p.GeneratedAtUtc);
        Assert.Null(p.PromptVersion);
    }

    // --- CP-02: Immutability (record equality) ---

    [Fact]
    public void Provenance_IsImmutable_SameValuesAreEqual()
    {
        var p1 = Provenance.Manual(NowUtc);
        var p2 = Provenance.Manual(NowUtc);

        Assert.Equal(p1, p2);
    }

    [Fact]
    public void Provenance_DifferentStatus_AreNotEqual()
    {
        var empty = Provenance.Empty;
        var manual = Provenance.Manual(NowUtc);

        Assert.NotEqual(empty, manual);
    }

    // --- CP-02: Provenance does not block construction ---

    [Fact]
    public void Provenance_WithAnyStatusValues_ConstructsWithoutException()
    {
        // Provenance should not impose workflow gates on status values
        var generated = new Provenance(ProvenanceSource.AI, ProvenanceStatus.Generated, NowUtc, "v1.0");
        var edited = new Provenance(ProvenanceSource.AI, ProvenanceStatus.Edited, null, null);

        Assert.Equal(ProvenanceStatus.Generated, generated.Status);
        Assert.Equal(ProvenanceStatus.Edited, edited.Status);
        Assert.Equal(NowUtc, generated.GeneratedAtUtc);
        Assert.Null(edited.GeneratedAtUtc);
    }

    // --- CP-02: ProvenanceStatus includes all 4 expected values ---

    [Fact]
    public void ProvenanceStatus_ContainsEmpty_Draft_Generated_Edited()
    {
        Assert.Equal(0, (int)ProvenanceStatus.Empty);
        Assert.True(Enum.IsDefined(typeof(ProvenanceStatus), ProvenanceStatus.Empty));
        Assert.True(Enum.IsDefined(typeof(ProvenanceStatus), ProvenanceStatus.Draft));
        Assert.True(Enum.IsDefined(typeof(ProvenanceStatus), ProvenanceStatus.Generated));
        Assert.True(Enum.IsDefined(typeof(ProvenanceStatus), ProvenanceStatus.Edited));
    }

    // --- CP-02: ProvenanceSource enum values ---

    [Fact]
    public void ProvenanceSource_ContainsManual_AndAI()
    {
        Assert.True(Enum.IsDefined(typeof(ProvenanceSource), ProvenanceSource.Manual));
        Assert.True(Enum.IsDefined(typeof(ProvenanceSource), ProvenanceSource.AI));
    }
}
