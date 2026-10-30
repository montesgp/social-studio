using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Infrastructure.Integrations;

namespace SocialStudio.Infrastructure.Ai.Providers;

/// <summary>
/// Model catalog for the host-local <c>claude-p</c> connector. The Claude Code CLI exposes no
/// model-enumeration command, so the model list is resolved as follows:
/// <list type="number">
/// <item>If an Anthropic API key is available (connector credential or the host <c>ANTHROPIC_API_KEY</c>
/// environment variable), query the live Anthropic <c>/v1/models</c> catalog — the same source as the
/// API-keyed Claude connector — so newly released models (e.g. Opus 4.8) appear automatically.</item>
/// <item>Otherwise (or if the live call fails), fall back to the curated current model set in
/// <see cref="ClaudePromptCliSettings.DefaultTextModels"/> so "Sync models" never errors.</item>
/// </list>
/// </summary>
internal sealed class ClaudePromptCliModelCatalogClient : IAiModelCatalogClient
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<string?> _apiKeyFromEnvironment;

    public ClaudePromptCliModelCatalogClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        Func<string?>? apiKeyFromEnvironment = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _apiKeyFromEnvironment = apiKeyFromEnvironment
            ?? (static () => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
    }

    public async Task<Result<IReadOnlyList<AiProviderModel>>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result.Success(CuratedModels());
        }

        var live = await new ClaudeModelCatalogClient(WithApiKey(apiKey), _httpClientFactory)
            .ListModelsAsync(cancellationToken);

        return live.IsSuccess && live.Value.Count > 0
            ? live
            : Result.Success(CuratedModels());
    }

    private string? ResolveApiKey()
    {
        if (ProviderCredentialReader.TryGet(_credential, "api_key", out var fromCredential))
        {
            return fromCredential;
        }

        var fromEnvironment = _apiKeyFromEnvironment();
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim();
    }

    private ResolvedProviderCredential WithApiKey(string apiKey) =>
        _credential with
        {
            Credentials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["api_key"] = apiKey,
            },
        };

    private static IReadOnlyList<AiProviderModel> CuratedModels() =>
        ClaudePromptCliSettings.DefaultTextModels
            .Select(id => new AiProviderModel(
                id,
                id,
                new Dictionary<string, string> { ["source"] = "curated" }))
            .ToList();
}
