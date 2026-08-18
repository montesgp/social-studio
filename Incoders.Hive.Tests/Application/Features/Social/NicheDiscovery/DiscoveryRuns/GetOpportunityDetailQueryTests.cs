using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>WU-11b (design §10): opportunity detail plus navigable evidence.</summary>
public sealed class GetOpportunityDetailQueryTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_ExistingOpportunity_ReturnsItPlusItsEvidence()
    {
        var record = OpportunityRecord();
        var evidence = new NicheDiscoveryEvidenceRecord(
            Guid.NewGuid(), record.RunId, record.Id, Org, NicheDiscoveryEvidenceKind.Video,
            "video-0", "channel-0", "Budget Travel Guide", null, null, Now, 10000, 200, 30, null, null, null, 480, "es",
            "ID", "youtube-data-api", Now);

        var handler = new GetOpportunityDetailHandler(
            new FakeOpportunityRepository(record), new FakeEvidenceRepository([evidence]));

        var result = await handler.Handle(new GetOpportunityDetailQuery(Org, record.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(record.Id, result.Value.Opportunity.Id);
        Assert.Single(result.Value.Evidence);
    }

    [Fact]
    public async Task Handle_UnknownOpportunity_ReturnsOpportunityNotFound()
    {
        var handler = new GetOpportunityDetailHandler(
            new FakeOpportunityRepository(null), new FakeEvidenceRepository([]));

        var result = await handler.Handle(new GetOpportunityDetailQuery(Org, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.OpportunityNotFound.Code, result.Error.Code);
    }

    private static NicheOpportunityRecord OpportunityRecord() => new(
        Guid.NewGuid(), Guid.NewGuid(), Org, null, "Budget travel", "Listicle", ["budget-travel"],
        0.7, 0.6, 0.9, "[]", 10000, 1.5, 0.2, 1.1, 3, 0.4, 0.3, 0.28, 1250.0, 0.5, 3, 3, 1, Now);

    private sealed class FakeOpportunityRepository : INicheOpportunityRepository
    {
        private readonly NicheOpportunityRecord? _record;

        public FakeOpportunityRepository(NicheOpportunityRecord? record) => _record = record;

        public Task AppendAsync(IReadOnlyList<NicheOpportunityRecord> opportunities, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<NicheOpportunityRecord>> ListByRunAsync(Guid organizationId, Guid runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NicheOpportunityRecord>>([]);

        public Task<NicheOpportunityRecord?> GetByIdAsync(Guid organizationId, Guid opportunityId, CancellationToken ct = default) =>
            Task.FromResult(_record is not null && _record.Id == opportunityId ? _record : null);
    }

    private sealed class FakeEvidenceRepository : INicheDiscoveryEvidenceRepository
    {
        private readonly List<NicheDiscoveryEvidenceRecord> _records;

        public FakeEvidenceRepository(List<NicheDiscoveryEvidenceRecord> records) => _records = records;

        public Task AppendAsync(IReadOnlyList<NicheDiscoveryEvidenceRecord> evidence, CancellationToken ct = default)
        {
            _records.AddRange(evidence);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NicheDiscoveryEvidenceRecord>> ListByOpportunityAsync(Guid organizationId, Guid opportunityId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NicheDiscoveryEvidenceRecord>>(_records.Where(e => e.OpportunityId == opportunityId).ToList());
    }
}
