using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.DiscoveryBankIntelligence;

namespace SocialStudio.Tests.Application.Social.DiscoveryBankIntelligence;

/// <summary>
/// The table half of the AI Analysis page. Its job is to be CHECKABLE: a stable order and an honest
/// total, so an operator can find the row the chat just cited.
/// </summary>
public sealed class ListDiscoveryBankHandlerTests
{
    private static readonly Guid OrgId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakeBankReader : IDiscoveryBankReader
    {
        private readonly IReadOnlyList<DiscoveryBankRow> _rows;

        public FakeBankReader(IReadOnlyList<DiscoveryBankRow> rows) => _rows = rows;

        public Task<IReadOnlyList<DiscoveryBankRow>> ListAsync(Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult(_rows);
    }

    private static DiscoveryBankRow Row(string key, DateTimeOffset observedAt) => new(
        key, "tiktok", ["fitness"], $"https://example.test/{key}", $"@{key}", $"Title {key}",
        PublishedAtUtc: null,
        ObservedAtUtc: observedAt,
        Views: 1, Likes: null, Comments: null, Shares: null, Saves: null, OpportunityScore: null);

    [Fact]
    public async Task ReturnsTheNewestObservationsFirst()
    {
        var rows = new[]
        {
            Row("old", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("new", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("mid", new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        var result = await new ListDiscoveryBankHandler(new FakeBankReader(rows))
            .Handle(new ListDiscoveryBankQuery(OrgId, 1, 50), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["new", "mid", "old"], result.Value.Items.Select(x => x.CandidateKey));
        Assert.Equal(3, result.Value.TotalCount);
    }

    [Fact]
    public async Task PagesWithoutLosingTheTrueTotal()
    {
        var rows = Enumerable.Range(0, 12)
            .Select(i => Row($"k{i:00}", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i)))
            .ToArray();

        var result = await new ListDiscoveryBankHandler(new FakeBankReader(rows))
            .Handle(new ListDiscoveryBankQuery(OrgId, 2, 5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value.TotalCount);
        Assert.Equal(5, result.Value.Items.Count);
        Assert.Equal("k06", result.Value.Items[0].CandidateKey);
    }

    [Fact]
    public async Task AnEmptyBankIsAnEmptyPage_NotAnError()
    {
        var result = await new ListDiscoveryBankHandler(new FakeBankReader([]))
            .Handle(new ListDiscoveryBankQuery(OrgId, 1, 50), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task AlwaysShipsTheSupportedQueryCatalog_SoTheChatCanSayWhatItAnswers()
    {
        var result = await new ListDiscoveryBankHandler(new FakeBankReader([]))
            .Handle(new ListDiscoveryBankQuery(OrgId, 1, 50), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DiscoveryBankQueryCatalog.Entries.Count, result.Value.SupportedQueries.Count);
    }
}
