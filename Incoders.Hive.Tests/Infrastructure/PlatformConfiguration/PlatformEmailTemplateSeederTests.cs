using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Domain.SystemSettings;
using Incoders.Hive.Infrastructure.Persistence.InMemory;
using Incoders.Hive.Infrastructure.PlatformConfiguration;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Infrastructure.PlatformConfiguration;

public sealed class PlatformEmailTemplateSeederTests
{
    private static readonly DateTime Now = new(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SeedAsync_WhenTemplatesMissing_CreatesAllDefaultTemplates()
    {
        var store = new InMemorySystemSettingStore();
        var repo = new InMemorySystemSettingRepository(store);
        var seeder = new PlatformEmailTemplateSeeder(
            repo,
            new InMemoryUnitOfWork(),
            new TestClock(Now));

        await seeder.SeedAsync();

        var globalItems = await repo.ListAsync(new SystemSettingQuery(
            Scope: SystemSettingScope.Global,
            TenantId: null,
            UserId: null,
            KeyContains: "email_templates.auth",
            Page: 1,
            PageSize: 20));

        Assert.Equal(4, globalItems.TotalCount);
        Assert.Contains(globalItems.Items, x => x.Key == "email_templates.auth.confirm_email");
        Assert.Contains(globalItems.Items, x => x.Key == "email_templates.auth.password_reset");
        Assert.Contains(globalItems.Items, x => x.Key == "email_templates.auth.magic_link");
        Assert.Contains(globalItems.Items, x => x.Key == "email_templates.auth.invite_user");

        var confirmSetting = globalItems.Items.Single(x => x.Key == "email_templates.auth.confirm_email");
        var payload = JsonSerializer.Deserialize<EmailTemplatePayload>(confirmSetting.Value, JsonOptions);
        Assert.NotNull(payload);
        Assert.Contains("{{confirmEmailUrl}}", payload!.HtmlBody);
        Assert.Contains("{{appName}}", payload.Subject);
        Assert.Contains("Incoders Hive", payload.HtmlBody);
        Assert.Contains("www.incoders.com.ar", payload.HtmlBody);
    }

    [Fact]
    public async Task SeedAsync_WhenTemplateAlreadyConfigured_DoesNotOverwriteExistingValue()
    {
        var store = new InMemorySystemSettingStore();
        var repo = new InMemorySystemSettingRepository(store);
        var customPayload = JsonSerializer.Serialize(
            new EmailTemplatePayload(
                Subject: "Custom subject",
                HtmlBody: "<p>Custom {{confirmEmailUrl}}</p>",
                TextBody: "Custom text"),
            JsonOptions);

        var existing = SystemSetting.Create(
            SystemSettingId.New(),
            "email_templates.auth.confirm_email",
            customPayload,
            SystemSettingScope.Global,
            null,
            null,
            Now);
        Assert.True(existing.IsSuccess);
        await repo.AddAsync(existing.Value);

        var seeder = new PlatformEmailTemplateSeeder(
            repo,
            new InMemoryUnitOfWork(),
            new TestClock(Now.AddMinutes(5)));

        await seeder.SeedAsync();

        var persisted = await repo.GetByKeyAsync(
            "email_templates.auth.confirm_email",
            SystemSettingScope.Global,
            null,
            null);

        Assert.NotNull(persisted);
        Assert.Equal(customPayload, persisted!.Value);
    }

    private sealed record EmailTemplatePayload(string Subject, string HtmlBody, string? TextBody);
}
