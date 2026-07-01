using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// WU-1 RED tests (task 1.2): <c>ComfyUiSettings.FromCredential</c> parses per-org credential
/// keys with safe defaults and validates the base URL. Refs: CUI-03, CUI-03-a, CUI-03-b; design §3.1.
/// </summary>
public sealed class ComfyUiSettingsTests
{
    [Fact]
    public void FromCredential_WithBaseUrlKey_ParsesSettingsWithComfyUiProviderKey()
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["base_url"] = "http://192.168.1.50:8188",
        }));

        Assert.True(result.IsSuccess);
        Assert.Equal("http://192.168.1.50:8188", result.Value.BaseUrl);
        Assert.Equal("comfyui", ComfyUiSettings.ProviderKey);
    }

    [Fact]
    public void FromCredential_MissingBaseUrl_DefaultsToLocalhost()
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>()));

        Assert.True(result.IsSuccess);
        Assert.Equal("http://127.0.0.1:8188", result.Value.BaseUrl);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com")]
    public void FromCredential_InvalidOrRelativeBaseUrl_ReturnsProviderCredentialUnavailable(string baseUrl)
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["base_url"] = baseUrl,
        }));

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderCredentialUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public void FromCredential_ParsesTimeoutAndPollIntervalAsPositiveInts()
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["timeout_ms"] = "60000",
            ["poll_interval_ms"] = "500",
        }));

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromMilliseconds(60000), result.Value.Timeout);
        Assert.Equal(TimeSpan.FromMilliseconds(500), result.Value.PollInterval);
    }

    [Fact]
    public void FromCredential_MissingTimeoutAndPollInterval_UsesDefaults()
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>()));

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromMilliseconds(180_000), result.Value.Timeout);
        Assert.Equal(TimeSpan.FromMilliseconds(1_000), result.Value.PollInterval);
    }

    [Theory]
    [InlineData("timeout_ms", "0")]
    [InlineData("timeout_ms", "not-a-number")]
    [InlineData("poll_interval_ms", "-1")]
    public void FromCredential_InvalidPositiveIntFields_ReturnsFailure(string key, string value)
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            [key] = value,
        }));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void FromCredential_MissingMaxReferenceImages_DefaultsToOne()
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>()));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.MaxReferenceImages);
    }

    [Fact]
    public void FromCredential_MissingOrEmptyWorkflowTemplateJson_SignalsBundledDefault()
    {
        var resultMissing = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>()));
        var resultEmpty = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["workflow_template_json"] = "",
        }));

        Assert.True(resultMissing.IsSuccess);
        Assert.Null(resultMissing.Value.WorkflowTemplateJson);
        Assert.True(resultEmpty.IsSuccess);
        Assert.Null(resultEmpty.Value.WorkflowTemplateJson);
    }

    [Fact]
    public void FromCredential_CustomWorkflowTemplateJson_IsPreserved()
    {
        const string customTemplate = """{"1":{"class_type":"CheckpointLoaderSimple"}}""";
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["workflow_template_json"] = customTemplate,
        }));

        Assert.True(result.IsSuccess);
        Assert.Equal(customTemplate, result.Value.WorkflowTemplateJson);
    }

    [Fact]
    public void FromCredential_DefaultOutputNodeId_IsNine()
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>()));

        Assert.True(result.IsSuccess);
        Assert.Equal("9", result.Value.OutputNodeId);
    }

    private static ResolvedProviderCredential BuildCredential(IReadOnlyDictionary<string, string> values) =>
        new(Guid.Empty, "comfyui", IntegrationConnectionMode.SystemDefault, values, SelectedModel: null);
}
