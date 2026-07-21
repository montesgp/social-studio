using System.Text.Json;
using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Infrastructure.Social.Publications;

/// <summary>
/// Native Instagram Graph API adapter implementing <see cref="IPublicationConnector"/>. All Graph API
/// HTTP/JSON types stay here — nothing beyond <see cref="ResolvedPublicationRequest"/> /
/// <see cref="Result{TValue}"/> crosses into Application.
///
/// <para>
/// Publish flow (two-step, per the Graph Content Publishing API): create a media container
/// (<c>POST /{ig-user-id}/media</c>) with the asset URL + caption, then publish it
/// (<c>POST /{ig-user-id}/media_publish</c>) with the resulting <c>creation_id</c>. v1 publishes a
/// single asset (the first entry of <see cref="ResolvedPublicationRequest.Assets"/>).
/// </para>
/// </summary>
public sealed class InstagramGraphPublicationConnector : IPublicationConnector
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v21.0";

    private readonly HttpClient _httpClient;

    public InstagramGraphPublicationConnector(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public string NetworkKey => "instagram";

    public async Task<Result<PublicationResult>> PublishAsync(ResolvedPublicationRequest request, CancellationToken ct)
    {
        if (request.Assets.Count == 0)
        {
            return Result.Failure<PublicationResult>(
                PublicationErrors.Validation("At least one asset is required to publish to Instagram."));
        }

        var asset = request.Assets[0];

        var containerResult = await CreateMediaContainerAsync(
            request.ExternalAccountId, request.AccessToken, asset.Url, request.Caption, ct);
        if (containerResult.IsFailure)
        {
            return Result.Failure<PublicationResult>(containerResult.Error);
        }

        var publishResult = await PublishMediaContainerAsync(
            request.ExternalAccountId, request.AccessToken, containerResult.Value, ct);

        return publishResult.IsFailure
            ? Result.Failure<PublicationResult>(publishResult.Error)
            : Result.Success(new PublicationResult(publishResult.Value));
    }

    private Task<Result<string>> CreateMediaContainerAsync(
        string externalAccountId, string accessToken, string imageUrl, string? caption, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["image_url"] = imageUrl,
            ["access_token"] = accessToken,
        };

        if (!string.IsNullOrWhiteSpace(caption))
        {
            form["caption"] = caption;
        }

        return PostGraphAsync($"{GraphApiBaseUrl}/{externalAccountId}/media", form, ct);
    }

    private Task<Result<string>> PublishMediaContainerAsync(
        string externalAccountId, string accessToken, string creationId, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["creation_id"] = creationId,
            ["access_token"] = accessToken,
        };

        return PostGraphAsync($"{GraphApiBaseUrl}/{externalAccountId}/media_publish", form, ct);
    }

    private async Task<Result<string>> PostGraphAsync(
        string url, Dictionary<string, string> form, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(form), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure<string>(PublicationErrors.ConnectorFailure("Instagram Graph API unreachable."));
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractErrorMessage(body) ?? $"Instagram Graph API returned {(int)response.StatusCode} {response.StatusCode}.";
            return Result.Failure<string>(PublicationErrors.ConnectorFailure(detail));
        }

        GraphMediaResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GraphMediaResponse>(body);
        }
        catch (JsonException)
        {
            return Result.Failure<string>(PublicationErrors.ConnectorFailure("Instagram Graph API returned an unparsable response."));
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Id))
        {
            return Result.Failure<string>(PublicationErrors.ConnectorFailure("Instagram Graph API response did not include a media id."));
        }

        return Result.Success(parsed.Id);
    }

    private static string? ExtractErrorMessage(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<GraphErrorEnvelope>(body);
            return error?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class GraphMediaResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;
    }

    private sealed class GraphErrorEnvelope
    {
        [JsonPropertyName("error")]
        public GraphErrorDetail? Error { get; init; }
    }

    private sealed class GraphErrorDetail
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("code")]
        public int Code { get; init; }
    }
}
