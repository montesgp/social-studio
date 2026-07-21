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
/// Publish flow (per the Graph Content Publishing API):
/// <list type="bullet">
///   <item><description><b>Single image</b> (<c>Assets.Count == 1</c>): create one media container
///   (<c>POST /{ig-user-id}/media</c>) with the asset URL + caption, then publish it
///   (<c>POST /{ig-user-id}/media_publish</c>) with the resulting <c>creation_id</c>.</description></item>
///   <item><description><b>Carousel</b> (<c>Assets.Count &gt; 1</c>): create one child media container
///   per asset with <c>is_carousel_item=true</c> (no caption on children), then a parent carousel
///   container (<c>media_type=CAROUSEL</c>, <c>children=&lt;child ids&gt;</c>, caption on the parent),
///   then publish the parent container the same way as the single-image flow.</description></item>
/// </list>
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

        var containerResult = request.Assets.Count == 1
            ? await CreateMediaContainerAsync(
                request.ExternalAccountId, request.AccessToken, request.Assets[0].Url, request.Caption,
                isCarouselItem: false, ct)
            : await CreateCarouselContainerAsync(request, ct);

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

    /// <summary>Creates one child container per asset (<c>is_carousel_item=true</c>, no caption), then
    /// the parent carousel container referencing all children (caption goes on the parent).</summary>
    private async Task<Result<string>> CreateCarouselContainerAsync(
        ResolvedPublicationRequest request, CancellationToken ct)
    {
        var childIds = new List<string>(request.Assets.Count);
        foreach (var asset in request.Assets)
        {
            var child = await CreateMediaContainerAsync(
                request.ExternalAccountId, request.AccessToken, asset.Url, caption: null,
                isCarouselItem: true, ct);
            if (child.IsFailure)
            {
                return child;
            }

            childIds.Add(child.Value);
        }

        var form = new Dictionary<string, string>
        {
            ["media_type"] = "CAROUSEL",
            ["children"] = string.Join(',', childIds),
            ["access_token"] = request.AccessToken,
        };

        if (!string.IsNullOrWhiteSpace(request.Caption))
        {
            form["caption"] = request.Caption;
        }

        return await PostGraphAsync($"{GraphApiBaseUrl}/{request.ExternalAccountId}/media", form, ct);
    }

    private Task<Result<string>> CreateMediaContainerAsync(
        string externalAccountId, string accessToken, string imageUrl, string? caption,
        bool isCarouselItem, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["image_url"] = imageUrl,
            ["access_token"] = accessToken,
        };

        if (isCarouselItem)
        {
            form["is_carousel_item"] = "true";
        }

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
