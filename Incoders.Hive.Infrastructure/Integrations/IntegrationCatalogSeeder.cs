using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;

namespace Incoders.Hive.Infrastructure.Integrations;

public sealed class IntegrationCatalogSeeder
{
    private static readonly IReadOnlyList<SeedDefinition> SeedDefinitions =
    [
        new(
            new Guid("18b44d7f-e85e-4a3d-95a3-4b2277d45501"),
            "openai",
            "OpenAI",
            IntegrationProviderCategory.AiModel,
            "AI model provider for generation and evaluation workflows, and image generation for ClientAesthetic creative slides.",
            "openai",
            true,
            true,
            false,
            false,
            [new IntegrationCredentialField("api_key", "API key", true, true, "sk-...", "Organization-scoped OpenAI API key")],
            ["gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano", "gpt-4o", "gpt-4o-mini", "o3-mini"],
            // Same connector, same api_key credential — also offered under Media generation providers.
            AdditionalCategories: [IntegrationProviderCategory.MediaGeneration],
            AdditionalModelsByCategory: new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>
            {
                [IntegrationProviderCategory.MediaGeneration] = ["dall-e-3", "gpt-image-1"],
            }),
        new(
            new Guid("f3df8bd4-3d68-4e03-b1d4-2ddbbdc3490c"),
            "gemini",
            "Gemini",
            IntegrationProviderCategory.AiModel,
            "Google Gemini model provider.",
            "gemini",
            true,
            true,
            false,
            false,
            [new IntegrationCredentialField("api_key", "API key", true, true, "AIza...", "Organization-scoped Gemini API key")],
            ["gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.0-flash", "gemini-1.5-pro"]),
        new(
            new Guid("ca29d4f8-f929-4d28-9d2f-e45f77967ae8"),
            "claude",
            "Claude",
            IntegrationProviderCategory.AiModel,
            "Anthropic Claude model provider.",
            "claude",
            true,
            true,
            false,
            false,
            [new IntegrationCredentialField("api_key", "API key", true, true, "sk-ant-...", "Organization-scoped Anthropic API key")],
            ["claude-opus-4-7", "claude-sonnet-4-6", "claude-haiku-4-5-20251001"]),
                new(
            new Guid("c4f126f4-7dd5-4e68-9f22-1e6f2ef0e101"),
            "claude-p",
            "Claude -p",
            IntegrationProviderCategory.AiModel,
            "Backend-local Claude Code CLI provider for text and media generation workflows. Runs on the application host; no per-organization credentials are required.",
            "claude",
            // BYOK + Managed + SystemDefault: claude-p is host-local, so SystemDefault ("use the host's
            // claude CLI") is the zero-config default. Host tuning (executable path, working/output
            // directories, timeout, tools…) is NOT an org credential — it falls back to the safe defaults
            // in ClaudePromptCliSettings on the host where the app runs.
            true,
            true,
            true,
            false,
            [],
            ClaudePromptCliSettings.DefaultTextModels,
            AdditionalCategories: [IntegrationProviderCategory.MediaGeneration],
            AdditionalModelsByCategory: new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>
            {
                [IntegrationProviderCategory.MediaGeneration] = ["claude-p-image-json"],
            }),        new(
            new Guid("b2610b54-a258-48d3-b2d2-12f70ac99d52"),
            "canva",
            "Canva",
            IntegrationProviderCategory.Design,
            "Canva Connect API app credentials (client-id + client-secret). OAuth user flow is B3.",
            "canva",
            true,
            false,
            false,
            false,
            [
                new IntegrationCredentialField("client_id",     "Client ID",     true,  true,  null,
                    "Canva Connect app client ID"),
                new IntegrationCredentialField("client_secret", "Client secret", true,  true,  null,
                    "Canva Connect app client secret"),
                new IntegrationCredentialField("scopes",        "Scopes",        false, false,
                    "asset:read asset:write brandtemplate:meta:read design:meta:read design:content:read design:content:write profile:read",
                    "Space-separated Connect API scopes. Leave blank to use the default set above.")
            ],
            []),
        new(
            new Guid("83b6f049-f598-47e9-a1c5-77d1f57cb694"),
            "apify",
            "Apify",
            IntegrationProviderCategory.DataExtraction,
            "Content discovery and scraping provider.",
            "apify",
            true,
            true,
            true,
            true,
            [
                new IntegrationCredentialField("api_key", "API key", true, true, null, "Apify API token/key for BYOK or managed mode"),
                new IntegrationCredentialField("actor_mappings_json", "Actor mappings", false, false, null, "JSON actor routing by social network")
            ],
            []),
        new(
            new Guid("9e024238-bd2f-4f74-b7f4-fab2f01f62d1"),
            "blotato",
            "Blotato",
            IntegrationProviderCategory.Publishing,
            "Publishing provider used by social publication workflows.",
            "blotato",
            true,
            false,
            false,
            false,
            [
                new IntegrationCredentialField("api_key", "API key", true, true, null, "Organization-scoped Blotato API key"),
                new IntegrationCredentialField("workspace_id", "Workspace id", false, false, null, "Optional workspace override")
            ],
            []),
        new(
            new Guid("3d3f7086-fbb6-41af-8bc8-c46d02d31001"),
            "google-drive",
            "Google Drive",
            IntegrationProviderCategory.Publishing,
            "Stores approved content packages in the configured administrator Drive.",
            "google-drive",
            true,
            true,
            false,
            false,
            [
                new IntegrationCredentialField("client_id", "Client ID", true, true, null, "OAuth client id for Google Drive API"),
                new IntegrationCredentialField("client_secret", "Client secret", true, true, null, "OAuth client secret for Google Drive API"),
                new IntegrationCredentialField("refresh_token", "Refresh token", true, true, null, "Refresh token for the target Drive account"),
                new IntegrationCredentialField("folder_id", "Folder ID", false, false, null, "Optional target folder for generated packages")
            ],
            []),
        new(
            new Guid("4f00fb80-7f54-45a4-8a57-dfbf2d320002"),
            "data365",
            "Data365",
            IntegrationProviderCategory.DataExtraction,
            "Social data provider for research, account discovery, and metrics retrieval.",
            "data365",
            true,
            true,
            false,
            false,
            [new IntegrationCredentialField("api_key", "API key", true, true, null, "Organization-scoped Data365 API key")],
            []),
        new(
            new Guid("4d19d1e0-62ef-4b88-a841-4c0280a90003"),
            "genspark-ai",
            "GenSpark AI",
            IntegrationProviderCategory.AiModel,
            "AI provider for generation, research, and content ideation workflows.",
            "genspark-ai",
            true,
            true,
            false,
            false,
            [new IntegrationCredentialField("api_key", "API key", true, true, null, "Organization-scoped GenSpark AI API key")],
            ["genspark-basic", "genspark-pro"]),
        new(
            new Guid("54d56310-30b4-4bda-b19e-69d6ac8d0004"),
            "tiktok",
            "TikTok",
            IntegrationProviderCategory.Publishing,
            "Social channel connector for future account insights and publishing workflows.",
            "tiktok",
            true,
            false,
            false,
            false,
            [
                new IntegrationCredentialField("access_token", "Access token", true, true, null, "OAuth access token for TikTok"),
                new IntegrationCredentialField("account_id", "Account ID", false, false, null, "Optional TikTok account identifier")
            ],
            []),
        new(
            new Guid("9fdc75bf-4f2f-4704-9b45-fec83e210005"),
            "youtube",
            "YouTube",
            IntegrationProviderCategory.Publishing,
            "Social channel connector for future account insights and publishing workflows.",
            "youtube",
            true,
            false,
            false,
            false,
            [
                new IntegrationCredentialField("client_id", "Client ID", true, true, null, "OAuth client id for YouTube Data API"),
                new IntegrationCredentialField("client_secret", "Client secret", true, true, null, "OAuth client secret for YouTube Data API"),
                new IntegrationCredentialField("refresh_token", "Refresh token", true, true, null, "Refresh token for the target YouTube channel")
            ],
            []),
        new(
            new Guid("7bda524a-1c87-4a6f-a6b0-f413e25c0006"),
            "instagram",
            "Instagram",
            IntegrationProviderCategory.Publishing,
            "Social channel connector for future account insights and publishing workflows.",
            "instagram",
            true,
            false,
            false,
            false,
            [
                new IntegrationCredentialField("access_token", "Access token", true, true, null, "Meta/Instagram access token"),
                new IntegrationCredentialField("account_id", "Account ID", false, true, null, "Instagram business account id")
            ],
            [])
    ];

    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public IntegrationCatalogSeeder(
        IIntegrationProviderCatalogRepository providers,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _providers = providers;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var seed in SeedDefinitions)
        {
            var categories = new List<IntegrationProviderCategory> { seed.Category };
            categories.AddRange(seed.AdditionalCategories ?? []);

            var modelsByCategory = new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>
            {
                [seed.Category] = seed.AvailableModels,
            };

            foreach (var (category, models) in seed.AdditionalModelsByCategory ?? new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>())
            {
                modelsByCategory[category] = models;
            }

            var createResult = IntegrationProvider.Create(
                new IntegrationProviderId(seed.Id),
                seed.Key,
                seed.DisplayName,
                categories,
                seed.Description,
                seed.IconKey,
                seed.SupportsBringYourOwnKey,
                seed.SupportsManagedByIncoders,
                seed.SupportsSystemDefault,
                seed.SystemDefaultReadOnly,
                seed.CredentialFields,
                modelsByCategory,
                _clock.UtcNow);

            if (createResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to seed integration provider '{seed.Key}': {createResult.Error.Code}");
            }

            await _providers.UpsertAsync(createResult.Value, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private sealed record SeedDefinition(
        Guid Id,
        string Key,
        string DisplayName,
        IntegrationProviderCategory Category,
        string? Description,
        string IconKey,
        bool SupportsBringYourOwnKey,
        bool SupportsManagedByIncoders,
        bool SupportsSystemDefault,
        bool SystemDefaultReadOnly,
        IReadOnlyList<IntegrationCredentialField> CredentialFields,
        IReadOnlyList<string> AvailableModels,
        IReadOnlyList<IntegrationProviderCategory>? AdditionalCategories = null,
        IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>>? AdditionalModelsByCategory = null);
}
