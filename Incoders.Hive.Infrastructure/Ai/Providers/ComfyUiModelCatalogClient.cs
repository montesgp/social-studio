using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

/// <summary>
/// Model catalog for the local <c>comfyui</c> connector. Queries <c>GET /object_info</c> and reads
/// the installed checkpoint enum (<c>CheckpointLoaderSimple.input.required.ckpt_name[0]</c>) plus
/// the optional IP-Adapter/LoRA loader enums, tagging each entry by <c>metadata.source</c>. On ANY
/// failure (invalid settings, unreachable server, non-2xx response, malformed JSON) it falls back to
/// <see cref="ComfyUiSettings.DefaultCheckpoints"/> as curated entries, so "Sync models" NEVER errors.
/// Same <see cref="Result{TValue}"/> contract as <see cref="ClaudePromptCliModelCatalogClient"/>.
/// Refs: CUI-04-a, CUI-04-b, CUI-04-c, CUI-INT-02; design §3.5, §0 OQ-3.
/// </summary>
internal sealed class ComfyUiModelCatalogClient : IAiModelCatalogClient
{
    private const string CheckpointSource = "checkpoint";
    private const string IpAdapterSource = "ipadapter";
    private const string LoraSource = "lora";
    private const string CuratedSource = "curated";

    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;

    public ComfyUiModelCatalogClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Result<IReadOnlyList<AiProviderModel>>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var settingsResult = ComfyUiSettings.FromCredential(_credential);
        if (settingsResult.IsFailure)
        {
            return Result.Success(CuratedFallback(ComfyUiSettings.CuratedDefaultCheckpoints));
        }

        var settings = settingsResult.Value;
        var baseUrl = settings.BaseUrl.TrimEnd('/');

        try
        {
            using var http = _httpClientFactory.CreateClient(nameof(ComfyUiModelCatalogClient));
            var response = await http.GetAsync($"{baseUrl}/object_info", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Success(CuratedFallback(settings.DefaultCheckpoints));
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var models = new List<AiProviderModel>();
            AppendEnumModels(doc.RootElement, "CheckpointLoaderSimple", "ckpt_name", CheckpointSource, models);
            AppendEnumModels(doc.RootElement, "IPAdapterModelLoader", "ipadapter_file", IpAdapterSource, models);
            AppendEnumModels(doc.RootElement, "LoraLoader", "lora_name", LoraSource, models);

            return Result.Success<IReadOnlyList<AiProviderModel>>(models);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // CUI-04-c: unreachable server, non-JSON response, or any other unexpected failure — never
            // propagate; the model-sync UX must keep working via the curated fallback.
            return Result.Success(CuratedFallback(settings.DefaultCheckpoints));
        }
    }

    private static void AppendEnumModels(
        JsonElement root,
        string nodeClassType,
        string enumFieldName,
        string source,
        List<AiProviderModel> models)
    {
        if (!root.TryGetProperty(nodeClassType, out var node)
            || !node.TryGetProperty("input", out var input)
            || !input.TryGetProperty("required", out var required)
            || !required.TryGetProperty(enumFieldName, out var field)
            || field.ValueKind != JsonValueKind.Array
            || field.GetArrayLength() == 0)
        {
            return;
        }

        var enumValues = field[0];
        if (enumValues.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in enumValues.EnumerateArray())
        {
            var name = item.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            models.Add(new AiProviderModel(name, name, new Dictionary<string, string> { ["source"] = source }));
        }
    }

    private static IReadOnlyList<AiProviderModel> CuratedFallback(IReadOnlyList<string> checkpoints) =>
        checkpoints
            .Select(name => new AiProviderModel(name, name, new Dictionary<string, string> { ["source"] = CuratedSource }))
            .ToList();
}
