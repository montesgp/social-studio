using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Domain.Integrations;

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
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("integrations.provider_mode_required", result.Error.Code);
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
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("google-drive", result.Value.IconKey);
    }
}
