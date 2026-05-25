using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Tests.Domain.Integrations;

public sealed class OrganizationIntegrationTests
{
    private static readonly DateTime Now = new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ReadOnlyRequiresSystemDefaultMode()
    {
        var result = OrganizationIntegration.Create(
            OrganizationIntegrationId.New(),
            Guid.NewGuid(),
            IntegrationProviderId.New(),
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.NotConfigured,
            isReadOnly: true,
            isDefaultAi: false,
            selectedAiModel: null,
            lastTestedAtUtc: null,
            lastError: null,
            nowUtc: Now);

        Assert.True(result.IsFailure);
        Assert.Equal("integrations.readonly_requires_system_default", result.Error.Code);
    }

    [Fact]
    public void MarkTestResult_FailureClearsDefaultAi()
    {
        var created = OrganizationIntegration.Create(
            OrganizationIntegrationId.New(),
            Guid.NewGuid(),
            IntegrationProviderId.New(),
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Connected,
            isReadOnly: false,
            isDefaultAi: true,
            selectedAiModel: "gpt-4.1-mini",
            lastTestedAtUtc: null,
            lastError: null,
            nowUtc: Now);

        Assert.True(created.IsSuccess);

        var integration = created.Value;
        integration.MarkTestResult(false, "bad credentials", Now.AddMinutes(1));

        Assert.Equal(IntegrationStatus.Failed, integration.Status);
        Assert.False(integration.IsDefaultAi);
        Assert.Equal("bad credentials", integration.LastError);
    }

    [Fact]
    public void Configure_EnabledMarksIntegrationConfiguredAndClearsDefaultAi()
    {
        var integration = OrganizationIntegration.Create(
            OrganizationIntegrationId.New(),
            Guid.NewGuid(),
            IntegrationProviderId.New(),
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Connected,
            isReadOnly: false,
            isDefaultAi: true,
            selectedAiModel: " gpt-4.1-mini ",
            lastTestedAtUtc: Now,
            lastError: "old error",
            nowUtc: Now).Value;

        var result = integration.Configure(
            IntegrationConnectionMode.BringYourOwnKey,
            isReadOnly: false,
            selectedAiModel: " gpt-4.1 ",
            enabled: true,
            Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(IntegrationStatus.Configured, integration.Status);
        Assert.False(integration.IsDefaultAi);
        Assert.Null(integration.LastError);
        Assert.Equal("gpt-4.1", integration.SelectedAiModel);
    }

    [Fact]
    public void Configure_DisabledMarksIntegrationDisabledAndClearsDefaultAi()
    {
        var integration = OrganizationIntegration.Create(
            OrganizationIntegrationId.New(),
            Guid.NewGuid(),
            IntegrationProviderId.New(),
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Connected,
            isReadOnly: false,
            isDefaultAi: true,
            selectedAiModel: "gpt-4.1-mini",
            lastTestedAtUtc: null,
            lastError: null,
            nowUtc: Now).Value;

        var result = integration.Configure(
            IntegrationConnectionMode.BringYourOwnKey,
            isReadOnly: false,
            selectedAiModel: null,
            enabled: false,
            Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(IntegrationStatus.Disabled, integration.Status);
        Assert.False(integration.IsDefaultAi);
    }

    [Fact]
    public void SetDefaultAi_RequiresConnectedStatus()
    {
        var integration = OrganizationIntegration.Create(
            OrganizationIntegrationId.New(),
            Guid.NewGuid(),
            IntegrationProviderId.New(),
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Configured,
            isReadOnly: false,
            isDefaultAi: false,
            selectedAiModel: null,
            lastTestedAtUtc: null,
            lastError: null,
            nowUtc: Now).Value;

        var result = integration.SetDefaultAi("gpt-4.1", Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("integrations.default_ai_provider_not_connected", result.Error.Code);
    }
}
