using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Social.Canva;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Social.Canva;

/// <summary>
/// WU-5 TDD tests for <see cref="CanvaOAuthService"/>.
///
/// <para>
/// All tests are fully offline — no Canva network calls.
/// Uses fake <see cref="HttpMessageHandler"/>, fake <see cref="ICanvaUserTokenRepository"/>,
/// fake <see cref="ICacheStore"/>, fake <see cref="IIntegrationResolver"/>,
/// and fake <see cref="IIntegrationSecretProtector"/>.
/// </para>
///
/// <para>
/// D2 xmin retry is covered by <c>EnsureFreshAsync_XminConflict_ReadsAgainDoesNotCallCanvaSecondTime</c>:
/// the fake repository throws <see cref="DbUpdateConcurrencyException"/> on the first
/// <c>UpsertAsync</c>; the service must reread the row once and use the already-rotated
/// token WITHOUT calling the Canva token endpoint a second time.
/// </para>
/// </summary>
public sealed class CanvaOAuthServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OrgIntegrationId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    // Use a fixed "now" that is deterministic regardless of wall-clock time.
    // Expired = Now - 2h ensures the stored token is always considered expired.
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 6, 6, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Now = FixedNow.UtcDateTime;
    private static readonly DateTime Expired = Now.AddHours(-2);

    // ─── PKCE helpers ────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildPkce_VerifierLength_IsWithin43And128Chars()
    {
        // Create a real CanvaOAuthService; call BuildAuthorizeUrlAsync to exercise PKCE generation.
        // We only need the verifier to be generated — we'll inspect the authorization URL query string.
        var (service, cache, _, _, _) = CreateService();

        var url = await service.BuildAuthorizeUrlAsync(UserId, OrgIntegrationId, OrgId, "https://app.local/callback", CancellationToken.None);

        Assert.True(url.IsSuccess, $"Expected success but got: {url.Error.Code}");

        // The verifier is stored in the cache, keyed by state which appears in the URL.
        var stateParam = ExtractQueryParam(url.Value, "state");
        Assert.NotNull(stateParam);

        var verifier = cache.GetVerifierForState(stateParam!);
        Assert.NotNull(verifier);
        Assert.True(verifier!.Length >= 43 && verifier.Length <= 128,
            $"Verifier length {verifier.Length} must be between 43 and 128");
    }

    [Fact]
    public async Task BuildPkce_Challenge_IsBase64UrlSha256OfVerifier()
    {
        var (service, cache, _, _, _) = CreateService();

        var url = await service.BuildAuthorizeUrlAsync(UserId, OrgIntegrationId, OrgId, "https://app.local/callback", CancellationToken.None);

        Assert.True(url.IsSuccess);
        var codeChallenge = ExtractQueryParam(url.Value, "code_challenge");
        var stateParam = ExtractQueryParam(url.Value, "state");
        var verifier = cache.GetVerifierForState(stateParam!);

        Assert.NotNull(codeChallenge);
        Assert.NotNull(verifier);

        // Compute expected challenge: BASE64URL(SHA256(verifier)) — no padding
        var expectedChallenge = ComputeBase64UrlSha256(verifier!);
        Assert.Equal(expectedChallenge, codeChallenge);
    }

    [Fact]
    public async Task BuildAuthorizeUrl_ContainsRequiredParams()
    {
        var (service, _, _, _, _) = CreateService();

        var result = await service.BuildAuthorizeUrlAsync(UserId, OrgIntegrationId, OrgId, "https://app.local/callback", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var url = result.Value;

        Assert.Contains("response_type=code", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("code_challenge=", url);
        Assert.Contains("state=", url);
        Assert.Contains("redirect_uri=", url);
    }

    [Fact]
    public async Task BuildAuthorizeUrl_EmptyScopesCredential_FallsBackToDefaultScopes()
    {
        // GIVEN an org that filled in client_id/secret but left the optional 'scopes' credential blank.
        var resolver = new ScopelessIntegrationResolver();
        var service = new CanvaOAuthService(
            new FakeCanvaUserTokenRepository(),
            new FakeCacheStore(),
            resolver,
            new FakeProtector(),
            new FakeUnitOfWork(),
            new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.OK, "{}")),
            new FakeTimeProvider(FixedNow));

        // WHEN the authorize URL is built.
        var result = await service.BuildAuthorizeUrlAsync(
            UserId, OrgIntegrationId, OrgId, "https://app.local/callback", CancellationToken.None);

        // THEN the request carries the default Connect scopes (never an empty scope).
        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error.Code}");
        var scope = ExtractQueryParam(result.Value, "scope");
        Assert.False(string.IsNullOrWhiteSpace(scope), "scope query param must not be empty");
        var decoded = Uri.UnescapeDataString(scope!);
        Assert.Contains("design:content:write", decoded);
    }

    [Fact]
    public async Task ExchangeCodeAsync_OnSuccess_CommitsUnitOfWork()
    {
        // Regression: the token Add MUST be committed. CanvaOAuthService previously never called
        // SaveChanges, so canva_user_tokens stayed empty after a successful authorize, which made
        // every export fail with canva.not_connected (409) even though the popup said "connected".
        var tokenResponse = new
        {
            access_token = "plaintext-access", // gates:secret-allow
            refresh_token = "plaintext-refresh", // gates:secret-allow
            expires_in = 3600,
            token_type = "Bearer",
            scope = "design:content:write",
        };
        var uow = new FakeUnitOfWork();
        var cache = new FakeCacheStore();
        var service = new CanvaOAuthService(
            new FakeCanvaUserTokenRepository(),
            cache,
            new FakeIntegrationResolver(),
            new FakeProtector(),
            uow,
            new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(tokenResponse))),
            new FakeTimeProvider(FixedNow));

        const string state = "commit-state";
        cache.SetPendingAuthorization(state,
            new CanvaPendingAuthorization("verifier-xyz", "https://app.local/callback", OrgId, OrgIntegrationId));

        var result = await service.ExchangeCodeAsync("auth-code", state, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error.Code}");
        Assert.True(uow.SaveChangesCallCount >= 1, "ExchangeCodeAsync must commit the token via the unit of work");
    }

    // ─── ExchangeCodeAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ExchangeCodeAsync_CallsTokenEndpoint_StoresEncryptedTokens()
    {
        var tokenResponse = new
        {
            access_token = "plaintext-access", // gates:secret-allow
            refresh_token = "plaintext-refresh", // gates:secret-allow
            expires_in = 3600,
            token_type = "Bearer",
            scope = "design:content:read design:content:write",
        };
        var httpHandler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(tokenResponse));

        var repo = new FakeCanvaUserTokenRepository();
        var protector = new FakeProtector();
        var (service, cache, _, _, _) = CreateService(httpHandler: httpHandler, repo: repo, protector: protector);

        // Pre-seed the cached authorization context (simulates authorize flow having run).
        var state = "test-state-123";
        var verifier = "test-verifier-abc";
        cache.SetPendingAuthorization(state,
            new CanvaPendingAuthorization(verifier, "https://app.local/callback", OrgId, OrgIntegrationId));

        var result = await service.ExchangeCodeAsync("auth-code", state, CancellationToken.None);

        Assert.True(result.IsSuccess, $"ExchangeCode failed: {result.Error.Code}");
        Assert.True(repo.UpsertCalled, "Expected UpsertAsync to be called");
        Assert.NotNull(repo.LastUpserted);

        // Assert tokens are stored as ciphertext, NOT plaintext
        Assert.NotEqual("plaintext-access", repo.LastUpserted!.EncryptedAccessToken);
        Assert.NotEqual("plaintext-refresh", repo.LastUpserted.EncryptedRefreshToken);
        Assert.Equal(protector.Protect("plaintext-access"), repo.LastUpserted.EncryptedAccessToken);
        Assert.Equal(protector.Protect("plaintext-refresh"), repo.LastUpserted.EncryptedRefreshToken);
    }

    [Fact]
    public async Task ExchangeCodeAsync_InvalidState_ReturnsFailure()
    {
        var (service, _, _, _, _) = CreateService();

        var result = await service.ExchangeCodeAsync("code", "wrong-state", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("canva.oauth.invalid_state", result.Error.Code);
    }

    // ─── EnsureFreshAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureFreshAsync_TokenNotExpired_DoesNotCallCanva()
    {
        var httpHandler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var repo = new FakeCanvaUserTokenRepository();
        var protector = new FakeProtector();
        var (service, _, _, _, _) = CreateService(httpHandler: httpHandler, repo: repo, protector: protector);

        // Store a non-expired token
        var token = CanvaUserToken.Create(
            CanvaUserTokenId.New(), UserId, OrgIntegrationId,
            protector.Protect("valid-access"),
            protector.Protect("valid-refresh"),
            Now.AddHours(1), // not expired
            "design:content:read",
            Now);
        repo.Seed(token);

        var result = await service.EnsureFreshAsync(UserId, OrgIntegrationId, OrgId, CancellationToken.None);

        Assert.True(result.IsSuccess, $"EnsureFreshAsync failed: {result.Error.Code}");
        Assert.Equal("valid-access", result.Value);
        Assert.Equal(0, httpHandler.CallCount); // Canva NEVER called
    }

    [Fact]
    public async Task EnsureFreshAsync_TokenExpired_RefreshesAndRotates()
    {
        var refreshResponse = new
        {
            access_token = "new-access-token", // gates:secret-allow
            refresh_token = "new-refresh-token", // gates:secret-allow
            expires_in = 3600,
            token_type = "Bearer",
            scope = "design:content:read design:content:write",
        };
        var httpHandler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(refreshResponse));

        var repo = new FakeCanvaUserTokenRepository();
        var protector = new FakeProtector();
        var (service, _, _, _, _) = CreateService(httpHandler: httpHandler, repo: repo, protector: protector);

        // Store an EXPIRED token
        var token = CanvaUserToken.Create(
            CanvaUserTokenId.New(), UserId, OrgIntegrationId,
            protector.Protect("old-access"),
            protector.Protect("old-refresh"),
            Expired, // EXPIRED
            "design:content:read",
            Now.AddDays(-1));
        repo.Seed(token);

        var result = await service.EnsureFreshAsync(UserId, OrgIntegrationId, OrgId, CancellationToken.None);

        Assert.True(result.IsSuccess, $"EnsureFreshAsync (refresh) failed: {result.Error.Code}");
        Assert.Equal("new-access-token", result.Value);
        Assert.Equal(1, httpHandler.CallCount); // Exactly ONE call to Canva
        Assert.True(repo.UpsertCalled, "Expected UpsertAsync to be called with rotated tokens");
        Assert.NotNull(repo.LastUpserted);
        Assert.Equal(protector.Protect("new-access-token"), repo.LastUpserted!.EncryptedAccessToken);
    }

    [Fact]
    public async Task EnsureFreshAsync_NoToken_ReturnsFailure()
    {
        var (service, _, _, _, _) = CreateService();

        var result = await service.EnsureFreshAsync(UserId, OrgIntegrationId, OrgId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("canva.oauth.token_not_found", result.Error.Code);
    }

    /// <summary>
    /// D2 retry test: fake repository throws <see cref="DbUpdateConcurrencyException"/>
    /// on the first <c>UpsertAsync</c>, then returns a freshly-rotated row on the
    /// re-read. The service must:
    /// 1. Call Canva token endpoint EXACTLY ONCE.
    /// 2. On conflict → re-read once (NOT call Canva again).
    /// 3. Return the access token from the re-read row (already rotated by the other writer).
    /// </summary>
    [Fact]
    public async Task EnsureFreshAsync_XminConflict_ReadsAgainDoesNotCallCanvaSecondTime()
    {
        var refreshResponse = new
        {
            access_token = "this-writers-access", // gates:secret-allow
            refresh_token = "this-writers-refresh", // gates:secret-allow
            expires_in = 3600,
            token_type = "Bearer",
            scope = "design:content:read",
        };
        var httpHandler = new FakeHttpMessageHandler(
            HttpStatusCode.OK, JsonSerializer.Serialize(refreshResponse));

        var protector = new FakeProtector();

        // The "already rotated" token that a concurrent writer stored:
        var alreadyRotatedToken = CanvaUserToken.Create(
            CanvaUserTokenId.New(), UserId, OrgIntegrationId,
            protector.Protect("concurrent-writers-fresh-access"),
            protector.Protect("concurrent-writers-fresh-refresh"),
            Now.AddHours(1), // not expired (already rotated by another writer)
            "design:content:read",
            Now);

        // Repo that throws DbUpdateConcurrencyException on first UpsertAsync,
        // then returns the already-rotated row on the second GetAsync.
        var repo = new XminConflictFakeRepository(
            initialToken: CanvaUserToken.Create(
                CanvaUserTokenId.New(), UserId, OrgIntegrationId,
                protector.Protect("old-access"),
                protector.Protect("old-refresh"),
                Expired, // expired → triggers refresh
                "design:content:read",
                Now.AddDays(-1)),
            rotatedToken: alreadyRotatedToken);

        var (service, _, _, _, _) = CreateService(httpHandler: httpHandler, repo: repo, protector: protector);

        var result = await service.EnsureFreshAsync(UserId, OrgIntegrationId, OrgId, CancellationToken.None);

        // The service must succeed — using the concurrent writer's already-rotated token
        Assert.True(result.IsSuccess, $"D2 retry failed: {result.Error.Code}");

        // Canva token endpoint called EXACTLY ONCE (the losing writer still called it,
        // but must not call it again after the conflict):
        Assert.Equal(1, httpHandler.CallCount);

        // The returned access token is from the row that WAS already in DB,
        // i.e. the concurrent writer's token (the service read it back and used it):
        Assert.Equal("concurrent-writers-fresh-access", result.Value);
    }

    // ─── RevokeAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAsync_DeletesTokenRow()
    {
        var repo = new FakeCanvaUserTokenRepository();
        var protector = new FakeProtector();
        var token = CanvaUserToken.Create(
            CanvaUserTokenId.New(), UserId, OrgIntegrationId,
            protector.Protect("access"), protector.Protect("refresh"),
            Now.AddHours(1), "design:content:read", Now);
        repo.Seed(token);

        var (service, _, _, _, _) = CreateService(repo: repo, protector: protector);

        await service.RevokeAsync(UserId, OrgIntegrationId, CancellationToken.None);

        Assert.True(repo.DeleteCalled, "Expected DeleteAsync to be called");
        Assert.Equal(UserId, repo.DeletedUserId);
        Assert.Equal(OrgIntegrationId, repo.DeletedOrgIntegrationId);
    }

    // ─── Factory ──────────────────────────────────────────────────────────────

    private static (CanvaOAuthService service, FakeCacheStore cache, FakeCanvaUserTokenRepository repo, FakeProtector protector, FakeHttpMessageHandler httpHandler) CreateService(
        FakeHttpMessageHandler? httpHandler = null,
        ICanvaUserTokenRepository? repo = null,
        FakeProtector? protector = null)
    {
        var handler = httpHandler ?? new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var repository = repo ?? new FakeCanvaUserTokenRepository();
        var prot = protector ?? new FakeProtector();
        var cache = new FakeCacheStore();

        var resolver = new FakeIntegrationResolver();
        var httpClient = new HttpClient(handler);

        // Use a deterministic time provider so tests are not sensitive to wall-clock time.
        // FixedNow = 2026-06-06 20:00 UTC, which is past all test "expired" values.
        var fakeTimeProvider = new FakeTimeProvider(FixedNow);

        var service = new CanvaOAuthService(
            repository,
            cache,
            resolver,
            prot,
            new FakeUnitOfWork(),
            httpClient,
            fakeTimeProvider);

        // We need to return a FakeCanvaUserTokenRepository for the tuple.
        // If a non-FakeCanvaUserTokenRepository was passed (e.g., XminConflictFakeRepository),
        // we return an empty fake (callers get the real repo via the service).
        var fakeRepo = repository as FakeCanvaUserTokenRepository ?? new FakeCanvaUserTokenRepository();
        return (service, cache, fakeRepo, prot, handler);
    }

    private static string ComputeBase64UrlSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(input));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string? ExtractQueryParam(string url, string key)
    {
        var uri = new Uri(url);
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == key)
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

/// <summary>Deterministic TimeProvider for tests — returns a fixed point in time.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>Simple fake for <see cref="ICacheStore"/> that exposes test-seeding methods.</summary>
internal sealed class FakeCacheStore : ICacheStore
{
    private readonly Dictionary<string, object?> _store = new();

    public void SetStateVerifier(string state, string verifier)
        => _store[$"canva:pkce:{state}"] = verifier;

    public void SetPendingAuthorization(string state, CanvaPendingAuthorization pending)
        => _store[$"canva:pkce:{state}"] = pending;

    public string? GetVerifierForState(string state)
    {
        _store.TryGetValue($"canva:pkce:{state}", out var v);
        return v is CanvaPendingAuthorization p ? p.Verifier : v as string;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(key, out var v);
        return Task.FromResult(v is T t ? t : default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>Fake repository — happy-path token seeding + upsert/delete tracking.</summary>
internal sealed class FakeCanvaUserTokenRepository : ICanvaUserTokenRepository
{
    private readonly Dictionary<(Guid, Guid), CanvaUserToken> _store = new();

    public bool UpsertCalled { get; private set; }
    public bool DeleteCalled { get; private set; }
    public CanvaUserToken? LastUpserted { get; private set; }
    public Guid DeletedUserId { get; private set; }
    public Guid DeletedOrgIntegrationId { get; private set; }

    public void Seed(CanvaUserToken token) => _store[(token.UserId, token.OrganizationIntegrationId)] = token;

    public Task<CanvaUserToken?> GetAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct = default)
    {
        _store.TryGetValue((userId, organizationIntegrationId), out var token);
        return Task.FromResult(token);
    }

    public Task UpsertAsync(CanvaUserToken token, CancellationToken ct = default)
    {
        UpsertCalled = true;
        LastUpserted = token;
        _store[(token.UserId, token.OrganizationIntegrationId)] = token;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct = default)
    {
        DeleteCalled = true;
        DeletedUserId = userId;
        DeletedOrgIntegrationId = organizationIntegrationId;
        _store.Remove((userId, organizationIntegrationId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// D2 xmin conflict fake: throws <see cref="DbUpdateConcurrencyException"/> on first
/// <c>UpsertAsync</c>, then returns the already-rotated token on the subsequent <c>GetAsync</c>.
/// </summary>
internal sealed class XminConflictFakeRepository : ICanvaUserTokenRepository
{
    private readonly CanvaUserToken _initialToken;
    private readonly CanvaUserToken _rotatedToken;
    private bool _conflictThrown;
    private bool _firstGetDone;

    public XminConflictFakeRepository(CanvaUserToken initialToken, CanvaUserToken rotatedToken)
    {
        _initialToken = initialToken;
        _rotatedToken = rotatedToken;
    }

    public Task<CanvaUserToken?> GetAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct = default)
    {
        // First call returns the expired token (pre-refresh read).
        // Second call (after conflict) returns the already-rotated token.
        if (!_firstGetDone)
        {
            _firstGetDone = true;
            return Task.FromResult<CanvaUserToken?>(_initialToken);
        }

        return Task.FromResult<CanvaUserToken?>(_rotatedToken);
    }

    public Task UpsertAsync(CanvaUserToken token, CancellationToken ct = default)
    {
        if (!_conflictThrown)
        {
            _conflictThrown = true;
            // Simulate the xmin row-version conflict that happens when another request
            // already committed a new refresh token between our read and our write.
            throw new DbUpdateConcurrencyException("Simulated xmin concurrency conflict");
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>
/// Simple pass-through protector for tests: wraps as "enc({value})" so assertions
/// can verify ciphertext != plaintext and can compute expected values.
/// </summary>
internal sealed class FakeProtector : IIntegrationSecretProtector
{
    public string Protect(string value) => $"enc({value})";
    public string Unprotect(string protectedValue)
        => protectedValue.StartsWith("enc(") && protectedValue.EndsWith(")")
            ? protectedValue[4..^1]
            : protectedValue;
}

/// <summary>Fake unit of work that records how many times the work was committed.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(0);
    }
}

/// <summary>Configurable fake HTTP handler for offline token endpoint tests.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public int CallCount { get; private set; }

    public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// Fake resolver that returns a canned "canva" integration with client_id/client_secret/scopes.
/// </summary>
internal sealed class FakeIntegrationResolver : IIntegrationResolver
{
    public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
        Guid organizationId,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var resolved = new ResolvedIntegration(
            organizationId,
            providerKey,
            "Canva",
            IntegrationProviderCategory.Design,
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Connected,
            IsReadOnly: false,
            SelectedAiModel: null,
            Credentials: new Dictionary<string, string>
            {
                ["client_id"] = "test-client-id",
                ["client_secret"] = "test-client-secret", // gates:secret-allow
                ["scopes"] = "design:content:read design:content:write",
            });

        return Task.FromResult(Result.Success(resolved));
    }
}

/// <summary>
/// Fake resolver whose Canva integration has client_id/client_secret but NO 'scopes' credential —
/// the exact production state that left the authorize URL with an empty scope.
/// </summary>
internal sealed class ScopelessIntegrationResolver : IIntegrationResolver
{
    public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
        Guid organizationId,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var resolved = new ResolvedIntegration(
            organizationId,
            providerKey,
            "Canva",
            IntegrationProviderCategory.Design,
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Connected,
            IsReadOnly: false,
            SelectedAiModel: null,
            Credentials: new Dictionary<string, string>
            {
                ["client_id"] = "test-client-id",
                ["client_secret"] = "test-client-secret", // gates:secret-allow
                // no "scopes" key on purpose
            });

        return Task.FromResult(Result.Success(resolved));
    }
}
