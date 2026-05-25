using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Tests.Infrastructure.Integrations;

/// <summary>
/// Pins the ordering contract of IntegrationResolver.ResolveDefaultAiProviderAsync:
///   1) is_default_ai=true + Connected wins unconditionally (first priority).
///   2) When no is_default_ai flag is set, the most-recently-configured (UpdatedAtUtc DESC)
///      Connected AiModel integration is selected — NOT the first returned by the repository.
/// </summary>
public sealed class IntegrationResolverDefaultAiOrderingTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // Shared provider id used by both the stub catalog and the integrations.
    private static readonly IntegrationProviderId AiProviderId = new(new Guid("aaaaaaaa-0000-0000-0000-000000000001"));

    // ─── TARGETED FIX: last-configured wins when no is_default_ai ────────────

    [Fact]
    public async Task ResolveDefaultAiProvider_WhenNoDefaultAiFlagSet_SelectsMostRecentlyUpdated()
    {
        // Arrange: two connected AiModel integrations, older returned first from repo.
        var olderUpdatedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var newerUpdatedAt = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc);

        var olderIntegration = MakeConnectedIntegration(
            isDefaultAi: false, model: "gpt-3.5", updatedAt: olderUpdatedAt);

        var newerIntegration = MakeConnectedIntegration(
            isDefaultAi: false, model: "gpt-4o", updatedAt: newerUpdatedAt);

        // Repo returns older first — without ordering the resolver would pick it.
        var repo = new StubIntegrationRepository([olderIntegration, newerIntegration]);
        var resolver = BuildResolver(repo);

        // Act
        var result = await resolver.ResolveDefaultAiProviderAsync(OrgId);

        // Assert: the newer integration (newerUpdatedAt) must win.
        Assert.True(result.IsSuccess);
        Assert.Equal("gpt-4o", result.Value.SelectedAiModel);
    }

    [Fact]
    public async Task ResolveDefaultAiProvider_IsDefaultAiTrueWins_EvenWhenOlderThanNonDefault()
    {
        // Arrange: default-AI integration is older than a non-default one.
        var olderUpdatedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var newerUpdatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        var defaultAiIntegration = MakeConnectedIntegration(
            isDefaultAi: true, model: "claude-3-opus", updatedAt: olderUpdatedAt);

        var newerNonDefault = MakeConnectedIntegration(
            isDefaultAi: false, model: "gpt-4o-mini", updatedAt: newerUpdatedAt);

        // Default AI is returned second in the list.
        var repo = new StubIntegrationRepository([newerNonDefault, defaultAiIntegration]);
        var resolver = BuildResolver(repo);

        // Act
        var result = await resolver.ResolveDefaultAiProviderAsync(OrgId);

        // Assert: is_default_ai=true takes priority regardless of UpdatedAtUtc.
        Assert.True(result.IsSuccess);
        Assert.Equal("claude-3-opus", result.Value.SelectedAiModel);
        Assert.True(result.Value.IsDefaultAi);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static OrganizationIntegration MakeConnectedIntegration(
        bool isDefaultAi, string? model, DateTime updatedAt) =>
        OrganizationIntegration.Restore(
            new OrganizationIntegrationId(Guid.NewGuid()),
            OrgId,
            AiProviderId,
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Connected,
            isReadOnly: false,
            isDefaultAi: isDefaultAi,
            selectedAiModel: model,
            lastTestedAtUtc: null,
            lastError: null,
            createdAtUtc: updatedAt,
            updatedAtUtc: updatedAt);

    private static IntegrationResolver BuildResolver(StubIntegrationRepository repo)
    {
        var providers = new StubProviderCatalogRepository();
        var credentials = new StubCredentialRepository();
        var protector = new NullProtector();
        return new IntegrationResolver(providers, repo, credentials, protector);
    }

    // ── Stubs ───────────────────────────────────────────────────────────────

    private sealed class StubProviderCatalogRepository : IIntegrationProviderCatalogRepository
    {
        // Must match AiProviderId used by the integrations in the outer class.
        private static readonly IntegrationProvider AiProvider = IntegrationProvider.Restore(
            AiProviderId,
            "openai",
            "OpenAI",
            IntegrationProviderCategory.AiModel,
            description: null,
            iconKey: "openai",
            supportsBringYourOwnKey: true,
            supportsManagedByIncoders: false,
            supportsSystemDefault: false,
            systemDefaultReadOnly: false,
            credentialFields: [],
            availableModels: [],
            isActive: true,
            createdAtUtc: DateTime.UtcNow,
            updatedAtUtc: DateTime.UtcNow);

        public Task<IReadOnlyList<IntegrationProvider>> ListActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IntegrationProvider>>([AiProvider]);

        public Task<IntegrationProvider?> GetByKeyAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<IntegrationProvider?>(AiProvider);

        public Task<IntegrationProvider?> GetByIdAsync(IntegrationProviderId id, CancellationToken ct = default) =>
            Task.FromResult<IntegrationProvider?>(AiProvider);

        public Task UpsertAsync(IntegrationProvider provider, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubIntegrationRepository : IOrganizationIntegrationRepository
    {
        private readonly IReadOnlyList<OrganizationIntegration> _integrations;

        public StubIntegrationRepository(IReadOnlyList<OrganizationIntegration> integrations)
            => _integrations = integrations;

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAndCategoryAsync(
            Guid organizationId,
            IntegrationProviderCategory category,
            CancellationToken ct = default) =>
            Task.FromResult(_integrations);

        public Task<OrganizationIntegration?> GetByOrganizationAndProviderAsync(
            Guid orgId, IntegrationProviderId providerId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAsync(
            Guid orgId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task AddAsync(OrganizationIntegration integration, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public void Update(OrganizationIntegration integration) =>
            throw new NotImplementedException();

        public void Remove(OrganizationIntegration integration) =>
            throw new NotImplementedException();
    }

    private sealed class StubCredentialRepository : IOrganizationIntegrationCredentialRepository
    {
        public Task<IReadOnlyList<OrganizationIntegrationCredential>> ListByIntegrationIdAsync(
            OrganizationIntegrationId id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationIntegrationCredential>>([]);

        public Task UpsertAsync(OrganizationIntegrationCredential credential, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task RemoveByIntegrationIdAsync(OrganizationIntegrationId id, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class NullProtector : IIntegrationSecretProtector
    {
        public string Protect(string value) => value;
        public string Unprotect(string protectedValue) => protectedValue;
    }
}
