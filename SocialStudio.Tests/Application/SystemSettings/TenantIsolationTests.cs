using SocialStudio.Application.Features.SystemSettings.Create;
using SocialStudio.Application.Features.SystemSettings.Delete;
using SocialStudio.Application.Features.SystemSettings.GetById;
using SocialStudio.Application.Features.SystemSettings.Update;
using SocialStudio.Domain.SystemSettings;

namespace SocialStudio.Tests.Application.SystemSettings;

public class TenantIsolationTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetById_WhenCrossTenantAccess_ReturnsFailure()
    {
        var h = new SystemSettingsTestHarness(Now);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var createdForA = await h.Create.Handle(
            new CreateSystemSettingCommand("feature.x", "on", SystemSettingScope.Tenant, tenantA, null),
            CancellationToken.None);

        var result = await h.GetById.Handle(
            new GetSystemSettingByIdQuery(createdForA.Value.Id, RequiredTenantId: tenantB),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("system_settings.cross_tenant_denied", result.Error.Code);
    }

    [Fact]
    public async Task Update_WhenCrossTenantAccess_ReturnsFailure()
    {
        var h = new SystemSettingsTestHarness(Now);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var created = await h.Create.Handle(
            new CreateSystemSettingCommand("feature.x", "on", SystemSettingScope.Tenant, tenantA, null),
            CancellationToken.None);

        var result = await h.Update.Handle(
            new UpdateSystemSettingCommand(created.Value.Id, "off", SystemSettingScope.Tenant, tenantB, null, RequiredTenantId: tenantB),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("system_settings.cross_tenant_denied", result.Error.Code);
    }

    [Fact]
    public async Task Delete_WhenCrossTenantAccess_ReturnsFailure()
    {
        var h = new SystemSettingsTestHarness(Now);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var created = await h.Create.Handle(
            new CreateSystemSettingCommand("feature.x", "on", SystemSettingScope.Tenant, tenantA, null),
            CancellationToken.None);

        var result = await h.Delete.Handle(
            new DeleteSystemSettingCommand(created.Value.Id, RequiredTenantId: tenantB),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("system_settings.cross_tenant_denied", result.Error.Code);
    }

    [Fact]
    public async Task GetById_GlobalScope_BypassesTenantCheck()
    {
        var h = new SystemSettingsTestHarness(Now);
        var tenantB = Guid.NewGuid();

        var created = await h.Create.Handle(
            new CreateSystemSettingCommand("global.x", "v", SystemSettingScope.Global, null, null),
            CancellationToken.None);

        var result = await h.GetById.Handle(
            new GetSystemSettingByIdQuery(created.Value.Id, RequiredTenantId: tenantB),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
