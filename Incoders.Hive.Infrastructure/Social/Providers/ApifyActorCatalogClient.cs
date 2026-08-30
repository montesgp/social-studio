using System.Net.Http.Headers;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Social.Providers;

/// <summary>
/// Anti-corruption adapter over Apify's actor list endpoint. It DISCOVERS what the connected
/// account can run; it never seeds, filters or defaults to a baked-in actor id (CD-12/D13).
///
/// <para>
/// The endpoint returns every Actor the account created OR used, which is exactly what the picker
/// needs: the organization routes to public-store actors it does not own. The <c>my</c> query
/// parameter would restrict the result to owned actors only, so it is deliberately never sent.
/// </para>
///
/// <para>
/// The API caps a page at 1000 records, so paging continues until a short page comes back.
/// Ownership is decided by comparing each actor's username with the authenticated account's own
/// username; when that lookup is unavailable the catalog still syncs, with nothing marked as owned.
/// </para>
/// </summary>
internal sealed class ApifyActorCatalogClient : IActorCatalogClient
{
    private const string ApiRoot = "https://api.apify.com/v2";
    private const int PageSize = 1000;
    private const int MaxPages = 50;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;

    public ApifyActorCatalogClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Result<IReadOnlyList<ProviderActor>>> ListActorsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var token)
            && !ProviderCredentialReader.TryGet(_credential, "api_token", out token))
        {
            return Result.Failure<IReadOnlyList<ProviderActor>>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        using var http = _httpClientFactory.CreateClient(nameof(ApifyActorCatalogClient));

        var ownUsername = await TryGetOwnUsernameAsync(http, token, cancellationToken);

        var actors = new List<ProviderActor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 0; page < MaxPages; page++)
        {
            var offset = page * PageSize;
            var url = $"{ApiRoot}/acts?limit={PageSize}&offset={offset}";

            var fetched = await SendAsync(http, url, token, cancellationToken);
            if (fetched.IsFailure)
            {
                return Result.Failure<IReadOnlyList<ProviderActor>>(fetched.Error);
            }

            ApifyActorListEnvelope? decoded;
            try
            {
                decoded = JsonSerializer.Deserialize<ApifyActorListEnvelope>(fetched.Value, JsonOptions);
            }
            catch (JsonException)
            {
                return Result.Failure<IReadOnlyList<ProviderActor>>(IntegrationErrors.ExternalProviderUnavailable);
            }

            var items = decoded?.Data?.Items ?? [];

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || !seen.Add(item.Id.Trim()))
                {
                    continue;
                }

                actors.Add(Map(item, ownUsername));
            }

            // The API never returns more than PageSize records, so a short page is the last page.
            if (items.Count < PageSize)
            {
                break;
            }
        }

        return Result.Success<IReadOnlyList<ProviderActor>>(actors);
    }

    private static ProviderActor Map(ApifyActorItem item, string? ownUsername)
    {
        var id = item.Id.Trim();
        var name = item.Name?.Trim() ?? string.Empty;
        var username = item.Username?.Trim() ?? string.Empty;
        var slug = string.IsNullOrEmpty(username) || string.IsNullOrEmpty(name)
            ? name
            : $"{username}/{name}";

        var displayName = !string.IsNullOrWhiteSpace(item.Title)
            ? item.Title.Trim()
            : !string.IsNullOrEmpty(slug) ? slug : id;

        var metadata = new Dictionary<string, string>
        {
            ["created_at"] = item.CreatedAt ?? string.Empty,
            ["modified_at"] = item.ModifiedAt ?? string.Empty,
        };

        var isOwned = !string.IsNullOrEmpty(ownUsername)
            && string.Equals(username, ownUsername, StringComparison.OrdinalIgnoreCase);

        return new ProviderActor(id, name, username, displayName, isOwned, metadata);
    }

    /// <summary>
    /// Best-effort: ownership is a display nicety, so a failure here must not fail the sync.
    /// </summary>
    private async Task<string?> TryGetOwnUsernameAsync(
        HttpClient http,
        string token,
        CancellationToken cancellationToken)
    {
        var fetched = await SendAsync(http, $"{ApiRoot}/users/me", token, cancellationToken);
        if (fetched.IsFailure)
        {
            return null;
        }

        try
        {
            var decoded = JsonSerializer.Deserialize<ApifyUserEnvelope>(fetched.Value, JsonOptions);
            var username = decoded?.Data?.Username?.Trim();
            return string.IsNullOrEmpty(username) ? null : username;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<Result<string>> SendAsync(
        HttpClient http,
        string url,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<string>(IntegrationErrors.ProviderTimeout);
        }
        catch (HttpRequestException)
        {
            return Result.Failure<string>(IntegrationErrors.ExternalProviderUnavailable);
        }

        using var responseHandle = response;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        return response.IsSuccessStatusCode
            ? Result.Success(payload)
            : Result.Failure<string>(MapStatus(response));
    }

    private static Error MapStatus(HttpResponseMessage response) =>
        (int)response.StatusCode switch
        {
            401 or 403 => IntegrationErrors.ProviderCredentialUnavailable,
            408 => IntegrationErrors.ProviderTimeout,
            429 => IntegrationErrors.ProviderRateLimited,
            _ => IntegrationErrors.ExternalProviderUnavailable,
        };

    private sealed record ApifyActorListEnvelope(ApifyActorListPage? Data);

    private sealed record ApifyActorListPage(IReadOnlyList<ApifyActorItem>? Items);

    private sealed record ApifyActorItem(
        string Id,
        string? Name,
        string? Username,
        string? Title,
        string? CreatedAt,
        string? ModifiedAt);

    private sealed record ApifyUserEnvelope(ApifyUser? Data);

    private sealed record ApifyUser(string? Username);
}
