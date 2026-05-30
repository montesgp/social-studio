using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Infrastructure adapter: Supabase Storage REST API.
/// This is the ONLY place in the codebase that sends HTTP requests to Supabase Storage
/// or uses a service_role key.  RLS bypass is confined here.
///
/// PUT  /storage/v1/object/{bucket}/{path}   — upload/overwrite (x-upsert: true)
/// POST /storage/v1/object/list/{bucket}     — list by prefix (JSON body)
/// GET  /storage/v1/object/{bucket}/{path}   — download
/// DEL  /storage/v1/object/{bucket}/{path}   — delete
/// </summary>
public sealed class SupabaseStorageAdapter : IGeneratedAssetStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SupabaseStorageOptions _options;

    public SupabaseStorageAdapter(HttpClient http, IOptions<SupabaseStorageOptions> options)
    {
        _http = http;
        _options = options.Value;
        ApplyDefaultHeaders(_http, _options.ServiceRoleKey);
    }

    // -----------------------------------------------------------------------
    // IGeneratedAssetStorage
    // -----------------------------------------------------------------------

    public async Task<StoredAssetRef> PutAssetAsync(
        StoredAssetKey key,
        StoredAssetContent content,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        ValidateRelativePath(key.RelativePath);

        var storagePath = BuildStoragePath(key);
        var url = BuildObjectUrl(storagePath);

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Add("x-upsert", "true");
        request.Content = new ByteArrayContent(content.Bytes);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(content.Mime);

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new GeneratedAssetStorageException(
                $"Supabase Storage PUT failed for '{storagePath}': {(int)response.StatusCode} — {body}");
        }

        var hash = ComputeSha256(content.Bytes);
        return new StoredAssetRef(storagePath, content.Mime, content.Bytes.Length, hash);
    }

    public async Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var path = folderPath.TrimEnd('/') + "/";
        var url = BuildObjectUrl(path);

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Add("x-upsert", "true");
        request.Content = new ByteArrayContent([]);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new GeneratedAssetStorageException(
                $"Supabase Storage folder placeholder PUT failed for '{path}': {(int)response.StatusCode} — {body}");
        }
    }

    public async Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var url = $"/storage/v1/object/list/{Uri.EscapeDataString(_options.Bucket)}";
        var allEntries = new List<StoredAssetEntry>();
        const int pageSize = 100;
        var offset = 0;

        while (true)
        {
            var body = JsonSerializer.Serialize(new { prefix, limit = pageSize, offset }, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new GeneratedAssetStorageException(
                    $"Supabase Storage LIST failed for prefix '{prefix}': {(int)response.StatusCode} — {errorBody}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var items = doc.RootElement.EnumerateArray().ToList();
            foreach (var item in items)
            {
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                long size = 0;
                string? mime = null;
                DateTimeOffset? updatedAt = null;

                if (item.TryGetProperty("metadata", out var meta))
                {
                    if (meta.TryGetProperty("size", out var sizeProp))
                    {
                        size = sizeProp.ValueKind == JsonValueKind.Number ? sizeProp.GetInt64() : 0;
                    }
                    if (meta.TryGetProperty("mimetype", out var mimeProp))
                    {
                        mime = mimeProp.GetString();
                    }
                }

                if (item.TryGetProperty("updated_at", out var updProp) && updProp.GetString() is { } updStr)
                {
                    DateTimeOffset.TryParse(updStr, out var dt);
                    updatedAt = dt == default ? null : dt;
                }

                var isFolder = name.EndsWith('/');
                var fullPath = string.IsNullOrEmpty(prefix)
                    ? name
                    : prefix.TrimEnd('/') + "/" + name;

                allEntries.Add(new StoredAssetEntry(fullPath, name, size, mime, updatedAt, isFolder));
            }

            if (items.Count < pageSize)
            {
                break;
            }

            offset += pageSize;
        }

        return allEntries;
    }

    public async Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(storagePath);

        var url = BuildObjectUrl(storagePath);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            return Result<StoredAssetStream>.Failure(
                Error.Failure("storage.open-failed", ex.Message));
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return Result<StoredAssetStream>.Failure(StorageErrors.NotFound);
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return Result<StoredAssetStream>.Failure(
                Error.Failure("storage.open-failed", $"HTTP {(int)response.StatusCode}"));
        }

        var mime = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var size = response.Content.Headers.ContentLength ?? 0;
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return Result<StoredAssetStream>.Success(new StoredAssetStream(stream, mime, size));
    }

    public async Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(storagePath);

        var url = BuildObjectUrl(storagePath);
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new GeneratedAssetStorageException(
                $"Supabase Storage DELETE failed for '{storagePath}': {(int)response.StatusCode} — {body}");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private string BuildObjectUrl(string path)
    {
        // Percent-encode each path segment but preserve / separators.
        var encodedPath = EncodePathPreservingSlashes(path);
        return $"/storage/v1/object/{Uri.EscapeDataString(_options.Bucket)}/{encodedPath}";
    }

    private static string BuildStoragePath(StoredAssetKey key) =>
        $"{key.OrganizationId}/{key.ClientId}/{key.AnalysisId}/{key.RelativePath}";

    private static string EncodePathPreservingSlashes(string path)
    {
        return string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
    }

    private static void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("RelativePath must not be empty.", nameof(relativePath));
        }

        // Prevent path traversal attacks.
        var segments = relativePath.Split('/');
        if (segments.Any(s => s == ".." || s == "."))
        {
            throw new ArgumentException(
                $"RelativePath must not contain '..' or '.' segments: '{relativePath}'",
                nameof(relativePath));
        }
    }

    private static void ApplyDefaultHeaders(HttpClient http, string serviceRoleKey)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        http.DefaultRequestHeaders.Add("apikey", serviceRoleKey);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// GeneratedAssetStorageException is defined in the Application port (IGeneratedAssetStorage.cs)
// to allow the Application pipeline to catch it without depending on Infrastructure.
