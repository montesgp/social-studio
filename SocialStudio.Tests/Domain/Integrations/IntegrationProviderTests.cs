using SocialStudio.Domain.Integrations;

namespace SocialStudio.Tests.Domain.Integrations;

public sealed class IntegrationProviderTests
{
    private static readonly DateTime Now = new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_RejectsDuplicateCredentialFieldKeys()
    {
        var result = IntegrationProvider.Create(
            IntegrationProviderId.New(),
            "openai",
            "OpenAI",
            IntegrationProviderCategory.AiModel,
            null,
            "bot",
            true,
            true,
            false,
            false,
            [
                new IntegrationCredentialField("api_key", "API key", true, true),
                new IntegrationCredentialField("api_key", "API key duplicate", true, false)
            ],
            [],
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("integrations.provider_credential_field_duplicate", result.Error.Code);
    }

    [Fact]
    public void Create_RequiresAtLeastOneSupportedMode()
    {
        var result = IntegrationProvider.Create(
            IntegrationProviderId.New(),
            "provider",
            "Provider",
            IntegrationProviderCategory.MediaGeneration,
            null,
            "sparkles",
            false,
            false,
            false,
            false,
            [],
            [],
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("integrations.provider_mode_required", result.Error.Code);
    }

    [Fact]
    public void Create_SingleCategory_BackCompat_ExposesCategoriesAndModelsByCategory()
    {
        var result = IntegrationProvider.Create(
            IntegrationProviderId.New(),
            "openai",
            "OpenAI",
            IntegrationProviderCategory.AiModel,
            null,
            "bot",
            true,
            true,
            false,
            false,
            [new IntegrationCredentialField("api_key", "API key", true, true)],
            ["gpt-4.1", "gpt-4o"],
            Now);

        Assert.True(result.IsSuccess);
        var provider = result.Value;
        Assert.Equal(IntegrationProviderCategory.AiModel, provider.Category);
        Assert.Equal([IntegrationProviderCategory.AiModel], provider.Categories);
        Assert.True(provider.ServesCategory(IntegrationProviderCategory.AiModel));
        Assert.False(provider.ServesCategory(IntegrationProviderCategory.MediaGeneration));
        Assert.Equal(["gpt-4.1", "gpt-4o"], provider.AvailableModels);
        Assert.Equal(["gpt-4.1", "gpt-4o"], provider.ModelsFor(IntegrationProviderCategory.AiModel));
        Assert.Empty(provider.ModelsFor(IntegrationProviderCategory.MediaGeneration));
    }

    [Fact]
    public void Create_MultiCategory_ExposesEachCategoryWithItsOwnModels()
    {
        var result = IntegrationProvider.Create(
            IntegrationProviderId.New(),
            "openai",
            "OpenAI",
            [IntegrationProviderCategory.AiModel, IntegrationProviderCategory.MediaGeneration],
            "Shared OpenAI connector.",
            "bot",
            true,
            true,
            false,
            false,
            [new IntegrationCredentialField("api_key", "API key", true, true)],
            new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>
            {
                [IntegrationProviderCategory.AiModel] = ["gpt-4.1", "gpt-4o"],
                [IntegrationProviderCategory.MediaGeneration] = ["dall-e-3", "gpt-image-1"],
            },
            Now);

        Assert.True(result.IsSuccess);
        var provider = result.Value;

        // Primary category (back-compat) is the first declared category.
        Assert.Equal(IntegrationProviderCategory.AiModel, provider.Category);
        Assert.Equal(
            [IntegrationProviderCategory.AiModel, IntegrationProviderCategory.MediaGeneration],
            provider.Categories);
        Assert.True(provider.ServesCategory(IntegrationProviderCategory.AiModel));
        Assert.True(provider.ServesCategory(IntegrationProviderCategory.MediaGeneration));
        Assert.Equal(["gpt-4.1", "gpt-4o"], provider.ModelsFor(IntegrationProviderCategory.AiModel));
        Assert.Equal(["dall-e-3", "gpt-image-1"], provider.ModelsFor(IntegrationProviderCategory.MediaGeneration));
    }

    [Fact]
    public void Create_MultiCategory_RejectsEmptyCategoryList()
    {
        var result = IntegrationProvider.Create(
            IntegrationProviderId.New(),
            "openai",
            "OpenAI",
            [],
            null,
            "bot",
            true,
            true,
            false,
            false,
            [new IntegrationCredentialField("api_key", "API key", true, true)],
            new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>(),
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("integrations.provider_category_required", result.Error.Code);
    }

    [Fact]
    public void Create_NormalizesIconKeyForFrontendProviderRendering()
    {
        var result = IntegrationProvider.Create(
            IntegrationProviderId.New(),
            "google-drive",
            "Google Drive",
            IntegrationProviderCategory.Publishing,
            "Publication package storage provider.",
            "  google-drive  ",
            true,
            false,
            false,
            false,
            [new IntegrationCredentialField("service_account_json", "Service account JSON", true, true)],
            [],
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("google-drive", result.Value.IconKey);
    }
}
