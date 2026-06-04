using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.History;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ProfileAnalyzer;

public sealed class ListProfileAnalysesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ClientLinkedAnalysis_ProjectsClientName()
    {
        var organizationId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var repository = new FakeProfileAnalysisRepository(
            new ProfileAnalysisHistoryPage(
                [
                    new ProfileAnalysisHistoryItem(
                        Guid.NewGuid(),
                        organizationId,
                        clientId,
                        Guid.NewGuid(),
                        "Acme Social",
                        "@acme",
                        "Analyzed",
                        null,
                        createdAt,
                        createdAt,
                        createdAt),
                ],
                TotalCount: 1,
                Page: 1,
                PageSize: 20));
        var handler = new ListProfileAnalysesQueryHandler(repository);

        var result = await handler.Handle(
            new ListProfileAnalysesQuery(organizationId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(clientId, item.ClientId);
        Assert.Equal("Acme Social", item.ClientName);
        Assert.Equal("@acme", item.ProfileUrlOrHandle);
    }

    private sealed class FakeProfileAnalysisRepository : IProfileAnalysisRepository
    {
        private readonly ProfileAnalysisHistoryPage _page;

        public FakeProfileAnalysisRepository(ProfileAnalysisHistoryPage page)
        {
            _page = page;
        }

        public Task AddAsync(ProfileAnalysis analysis, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ProfileAnalysis?> GetByIdAsync(
            Guid analysisId,
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ProfileAnalysis?>(null);

        public Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(
            Guid organizationId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_page);

        public Task UpdateAsync(ProfileAnalysis analysis, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> DeleteAsync(
            Guid analysisId,
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
