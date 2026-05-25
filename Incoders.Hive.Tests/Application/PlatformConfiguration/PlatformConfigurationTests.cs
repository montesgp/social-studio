using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Features.PlatformConfiguration.EmailTemplates;
using Incoders.Hive.Application.Features.PlatformConfiguration.Integrations;
using Incoders.Hive.Infrastructure.Persistence.InMemory;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.PlatformConfiguration;

public sealed class PlatformConfigurationTests
{
    private static readonly DateTime Now = new(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EmailTemplate_upsert_persists_configured_template()
    {
        var harness = new PlatformConfigurationHarness();

        var saved = await harness.UpsertTemplate.Handle(
            new UpsertEmailTemplateCommand(
                "auth.confirm-email",
                "Confirm now",
                "<p>Confirm {{confirmEmailUrl}}</p>",
                "Confirm {{confirmEmailUrl}}"),
            CancellationToken.None);

        var listed = await harness.GetTemplates.Handle(new GetEmailTemplatesQuery(), CancellationToken.None);
        var template = listed.Value.Single(x => x.Key == "auth.confirm-email");

        Assert.True(saved.IsSuccess);
        Assert.Equal("Confirm now", template.Subject);
        Assert.True(template.IsConfigured);
        Assert.Contains("{{confirmEmailUrl}}", template.Variables);
    }

    [Fact]
    public async Task EmailTemplate_rejects_unknown_key()
    {
        var harness = new PlatformConfigurationHarness();

        var saved = await harness.UpsertTemplate.Handle(
            new UpsertEmailTemplateCommand("auth.unknown", "Subject", "<p>Body</p>", null),
            CancellationToken.None);

        Assert.True(saved.IsFailure);
    }

    [Fact]
    public async Task Smtp_upsert_protects_password_and_returns_configured_flag()
    {
        var harness = new PlatformConfigurationHarness();

        var saved = await harness.UpsertSmtp.Handle(
            new UpsertEmailSmtpConfigurationCommand(
                true,
                "smtp.example.com",
                587,
                true,
                "mailer",
                "fixture-value-123",
                "no-reply@example.com",
                "Incoders Hive"),
            CancellationToken.None);

        var loaded = await harness.GetSmtp.Handle(new GetEmailSmtpConfigurationQuery(), CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.True(loaded.Value.PasswordConfigured);
        Assert.Equal("smtp.example.com", loaded.Value.Host);
        Assert.DoesNotContain("fixture-value-123", harness.StoredSettingsText());
        Assert.Contains("encoded::", harness.StoredSettingsText());
    }

    [Fact]
    public async Task Smtp_update_without_password_preserves_existing_secret()
    {
        var harness = new PlatformConfigurationHarness();
        await harness.UpsertSmtp.Handle(
            new UpsertEmailSmtpConfigurationCommand(
                true,
                "smtp.example.com",
                587,
                true,
                "mailer",
                "fixture-value-123",
                "no-reply@example.com",
                "Incoders Hive"),
            CancellationToken.None);

        var updated = await harness.UpsertSmtp.Handle(
            new UpsertEmailSmtpConfigurationCommand(
                false,
                "smtp2.example.com",
                2525,
                false,
                "mailer2",
                null,
                "no-reply2@example.com",
                "Hive"),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.True(updated.Value.PasswordConfigured);
        Assert.Contains("encoded::", harness.StoredSettingsText());
    }

    private sealed class PlatformConfigurationHarness
    {
        private readonly InMemorySystemSettingStore _store = new();

        public PlatformConfigurationHarness()
        {
            var repo = new InMemorySystemSettingRepository(_store);
            var unitOfWork = new InMemoryUnitOfWork();
            var clock = new TestClock(Now);
            var protector = new FakeSecretProtector();

            GetTemplates = new GetEmailTemplatesHandler(repo);
            UpsertTemplate = new UpsertEmailTemplateHandler(repo, unitOfWork, clock);
            GetSmtp = new GetEmailSmtpConfigurationHandler(repo);
            UpsertSmtp = new UpsertEmailSmtpConfigurationHandler(repo, protector, unitOfWork, clock);
        }

        public GetEmailTemplatesHandler GetTemplates { get; }
        public UpsertEmailTemplateHandler UpsertTemplate { get; }
        public GetEmailSmtpConfigurationHandler GetSmtp { get; }
        public UpsertEmailSmtpConfigurationHandler UpsertSmtp { get; }

        public string StoredSettingsText() =>
            string.Join("\n", _store.Settings.Values.Select(x => x.Value));
    }

    private sealed class FakeSecretProtector : IIntegrationSecretProtector
    {
        public string Protect(string value) => $"encoded::{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))}";

        public string Unprotect(string protectedValue) =>
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
                protectedValue.Replace("encoded::", string.Empty, StringComparison.Ordinal)));
    }
}
