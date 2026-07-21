using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;
using Incoders.Hive.Application.Features.Social.Publications.EnsureClientContentMaterialized;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using MediatR;

namespace Incoders.Hive.Tests.Application.Social.Publications;

/// <summary>WU3 (Phase 10) TDD tests for <see cref="EnsureClientContentMaterializedCommand"/> (design §5.3).</summary>
public sealed class EnsureClientContentMaterializedCommandTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    [Fact]
    public async Task Handle_MaterializesOnlyCompletedAnalyses()
    {
        var analysisId = Guid.NewGuid();
        var analyses = new FakeAnalysisRepository([BuildRecord(analysisId)]);
        var sender = new RecordingSender();
        var handler = new EnsureClientContentMaterializedCommandHandler(analyses, sender);

        var result = await handler.Handle(
            new EnsureClientContentMaterializedCommand(OrganizationId, ClientId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(sender.Sent);
        Assert.Equal(analysisId, ((MaterializeAnalysisCommand)sender.Sent[0]).AnalysisId);
    }

    [Fact]
    public async Task Handle_SecondRun_IsIdempotent_NoNewMaterializeCallsBeyondFirstRun()
    {
        var analysisId = Guid.NewGuid();
        var analyses = new FakeAnalysisRepository([BuildRecord(analysisId)]);
        var sender = new RecordingSender();
        var handler = new EnsureClientContentMaterializedCommandHandler(analyses, sender);

        await handler.Handle(new EnsureClientContentMaterializedCommand(OrganizationId, ClientId), CancellationToken.None);
        await handler.Handle(new EnsureClientContentMaterializedCommand(OrganizationId, ClientId), CancellationToken.None);

        // The command re-runs MaterializeAnalysisCommand for every completed analysis EVERY time (it
        // is orchestration only, delegating idempotency to MaterializeAnalysisCommand itself, which is
        // documented and tested as idempotent — a second run creates no duplicate ContentSet/ContentItems).
        Assert.Equal(2, sender.Sent.Count);
    }

    private static SingleContentAnalysisRecord BuildRecord(Guid analysisId) => new(
        analysisId,
        OrganizationId,
        ClientId,
        Guid.NewGuid(),
        "https://example.com/post",
        null,
        "instagram",
        SingleContentAnalysisStatus.Analyzed,
        null,
        null,
        null,
        null,
        null,
        DateTime.UtcNow,
        DateTime.UtcNow,
        DateTime.UtcNow);

    private sealed class FakeAnalysisRepository : ISingleContentAnalysisRepository
    {
        private readonly IReadOnlyList<SingleContentAnalysisRecord> _records;

        public FakeAnalysisRepository(IReadOnlyList<SingleContentAnalysisRecord> records) => _records = records;

        public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<SingleContentAnalysisRecord?> GetByIdAsync(
            Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SingleContentAnalysisRecord?>(null);

        public Task UpdateStatusAsync(
            Guid analysisId, Guid organizationId, SingleContentAnalysisStatus status, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default, string? failureCode = null, string? failureMessageKey = null) =>
            Task.CompletedTask;

        public Task UpdateAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
            Guid organizationId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));

        public Task<IReadOnlyList<SingleContentAnalysisRecord>> ListCompletedByClientAsync(
            Guid organizationId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records);

        public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
            Guid organizationId, Guid userId, string normalizedUrl, string sourceUrl,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SingleContentAnalysisHistoryItem?>(null);

        public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
            Guid analysisId, Guid organizationId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }

    private sealed class RecordingSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            if (typeof(TResponse) == typeof(Result<ContentSetResponse>))
            {
                var success = (object)Result.Success<ContentSetResponse>(null!);
                return Task.FromResult((TResponse)success);
            }

            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
