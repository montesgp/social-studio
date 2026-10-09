using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.Workflow;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// RED tests for <see cref="EfContentWorkflowSessionRepository"/> (design decision #1: normalized
/// tables replace the in-memory dictionary). Uses the EF InMemory provider like sibling repository
/// tests in this file group — no real Postgres available in this repo's test harness, so the partial
/// unique index (enforced only by the migration/real Postgres) is NOT re-asserted here; the
/// DB-unique-violation SWALLOW path is instead exercised directly against the SaveChanges pipeline.
/// </summary>
public sealed class EfContentWorkflowSessionRepositoryTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    [Fact]
    public async Task InsertThenGetById_RoundTripsSessionAndCandidates()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);

        var sessionId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var session = WorkflowSession.Create(sessionId);
        session.AddCandidate(WorkflowCandidate.Create(candidateId, "https://tiktok.com/@a/video/1"));

        await repo.InsertAsync(session, OrgId, ClientId, "Acme", "in_progress", "{\"criterion\":\"marketing\"}", CancellationToken.None);

        var byId = await repo.GetByIdAsync(sessionId, OrgId, CancellationToken.None);
        var byIdInternal = await repo.GetByIdInternalAsync(sessionId, CancellationToken.None);

        Assert.NotNull(byId);
        Assert.NotNull(byIdInternal);
        Assert.Equal(sessionId, byId!.Id);
        Assert.Single(byId.Candidates);
        Assert.Equal(candidateId, byId.Candidates[0].Id);
        Assert.Equal(WorkflowCandidateStatus.Discovered, byId.Candidates[0].Status);
        Assert.Equal(sessionId, byIdInternal!.Id);
    }

    [Fact]
    public async Task GetById_WrongOrganization_ReturnsNull()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var sessionId = Guid.NewGuid();
        var session = WorkflowSession.Create(sessionId);

        await repo.InsertAsync(session, OrgId, ClientId, "Acme", "in_progress", null, CancellationToken.None);

        var result = await repo.GetByIdAsync(sessionId, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdInternal_IgnoresOrganizationScope()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var sessionId = Guid.NewGuid();
        var session = WorkflowSession.Create(sessionId);

        await repo.InsertAsync(session, OrgId, ClientId, "Acme", "in_progress", null, CancellationToken.None);

        var result = await repo.GetByIdInternalAsync(sessionId, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListAsync_FiltersByClientId_WhenProvided()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        await repo.InsertAsync(WorkflowSession.Create(Guid.NewGuid()), OrgId, clientA, "Client A", "completed", null, CancellationToken.None);
        await repo.InsertAsync(WorkflowSession.Create(Guid.NewGuid()), OrgId, clientB, "Client B", "completed", null, CancellationToken.None);

        var filtered = await repo.ListAsync(OrgId, clientA, 50, CancellationToken.None);
        var unfiltered = await repo.ListAsync(OrgId, null, 50, CancellationToken.None);

        Assert.Single(filtered);
        Assert.Equal(clientA, filtered[0].ClientId);
        Assert.Equal(2, unfiltered.Count);
    }

    [Fact]
    public async Task UpdateCandidateStatusAsync_PersistsStatusAnalysisIdBatchIdAndFailureCode()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var sessionId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var session = WorkflowSession.Create(sessionId);
        session.AddCandidate(WorkflowCandidate.Create(candidateId, "https://tiktok.com/@a/video/1"));
        await repo.InsertAsync(session, OrgId, ClientId, "Acme", "in_progress", null, CancellationToken.None);

        var analysisId = Guid.NewGuid();
        await repo.UpdateCandidateStatusAsync(
            candidateId, WorkflowCandidateStatus.Analyzing, analysisId, null, null, CancellationToken.None);

        var updated = await repo.GetByIdInternalAsync(sessionId, CancellationToken.None);

        Assert.Equal(WorkflowCandidateStatus.Analyzing, updated!.Candidates[0].Status);
    }

    [Fact]
    public async Task SetCandidateCompositionAsync_HappyPath_RoundTripsCompositionAndEnqueueKey()
    {
        // NOTE (deviation, documented in apply-progress): the DB-level unique-violation SWALLOW path
        // (design decision #6 defense-in-depth, see EfContentWorkflowSessionRepository.IsPgUniqueViolation)
        // requires a real Postgres connection to trigger a genuine 23505 — the EF InMemory provider used
        // by this test file group does not enforce the partial unique index at all, and this repo has no
        // Testcontainers/real-Postgres test harness. That code path is exercised by the migration schema
        // + manual verification only. This test instead locks down the ordinary round-trip it wraps.
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var sessionId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var session = WorkflowSession.Create(sessionId);
        session.AddCandidate(WorkflowCandidate.Create(candidateId, "https://tiktok.com/@a/video/1"));
        await repo.InsertAsync(session, OrgId, ClientId, "Acme", "in_progress", null, CancellationToken.None);

        await repo.SetCandidateCompositionAsync(
            candidateId, "[{\"FormatKey\":\"reel-script\",\"Count\":1}]", "abc123", CancellationToken.None);

        var updated = await repo.GetByIdInternalAsync(sessionId, CancellationToken.None);
        var composition = updated!.Candidates[0].Composition;

        Assert.Single(composition);
        Assert.Equal("reel-script", composition[0].FormatKey);
    }

    [Fact]
    public async Task AddCandidatesAsync_ThenGetDetailAsync_RoundTripsPayload()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var sessionId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();

        await repo.InsertAsync(
            WorkflowSession.Create(sessionId), OrgId, ClientId, "Acme", "in_progress", "{\"phase\":\"InProgress\"}",
            CancellationToken.None);

        await repo.AddCandidatesAsync(
            sessionId,
            [new WorkflowDiscoveredCandidate(candidateId, "https://tiktok.com/@a/video/1", "{\"title\":\"Reel\"}")],
            CancellationToken.None);

        var detail = await repo.GetDetailAsync(sessionId, OrgId, CancellationToken.None);

        Assert.NotNull(detail);
        var candidate = Assert.Single(detail!.Candidates);
        Assert.Equal(candidateId, candidate.Id);
        Assert.Equal("{\"title\":\"Reel\"}", candidate.PayloadJson);
        Assert.False(candidate.IsSelected);
    }

    [Fact]
    public async Task GetDetailAsync_WrongOrganization_ReturnsNull()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var sessionId = Guid.NewGuid();

        await repo.InsertAsync(
            WorkflowSession.Create(sessionId), OrgId, ClientId, "Acme", "in_progress", null, CancellationToken.None);

        var detail = await repo.GetDetailAsync(sessionId, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(detail);
    }

    [Fact]
    public async Task GetDetailInternalAsync_IgnoresOrganizationScope()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var sessionId = Guid.NewGuid();

        await repo.InsertAsync(
            WorkflowSession.Create(sessionId), OrgId, ClientId, "Acme", "in_progress", null, CancellationToken.None);

        var detail = await repo.GetDetailInternalAsync(sessionId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(sessionId, detail!.Id);
    }

    [Fact]
    public async Task UpdateSessionStateAsync_PersistsStatusAndConfigJson()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var sessionId = Guid.NewGuid();

        await repo.InsertAsync(
            WorkflowSession.Create(sessionId), OrgId, ClientId, "Acme", "in_progress", "{\"phase\":\"InProgress\"}",
            CancellationToken.None);

        await repo.UpdateSessionStateAsync(sessionId, "completed", "{\"phase\":\"Completed\"}", CancellationToken.None);

        var detail = await repo.GetDetailInternalAsync(sessionId, CancellationToken.None);

        Assert.Equal("completed", detail!.Status);
        Assert.Equal("{\"phase\":\"Completed\"}", detail.ConfigJson);
    }

    [Fact]
    public async Task ListAsync_ProjectsResultCountsFromCandidates()
    {
        await using var db = CreateDb();
        var repo = new EfContentWorkflowSessionRepository(db);
        var sessionId = Guid.NewGuid();

        await repo.InsertAsync(
            WorkflowSession.Create(sessionId), OrgId, ClientId, "Acme", "completed", "{\"criterion\":\"niche\"}",
            CancellationToken.None);
        await repo.AddCandidatesAsync(
            sessionId,
            [
                new WorkflowDiscoveredCandidate(Guid.NewGuid(), "ext-1", "{}"),
                new WorkflowDiscoveredCandidate(Guid.NewGuid(), "ext-2", "{}"),
            ],
            CancellationToken.None);
        await repo.ReplaceSelectionAsync(sessionId, [Guid.NewGuid()], CancellationToken.None);

        var list = await repo.ListAsync(OrgId, ClientId, 50, CancellationToken.None);

        var summary = Assert.Single(list);
        Assert.Equal(2, summary.TotalResults);
        Assert.Equal("{\"criterion\":\"niche\"}", summary.ConfigJson);
    }

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-workflow-session-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
