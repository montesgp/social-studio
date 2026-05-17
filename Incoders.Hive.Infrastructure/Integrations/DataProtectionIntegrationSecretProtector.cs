using Incoders.Hive.Application.Abstractions.Integrations;
using Microsoft.AspNetCore.DataProtection;

namespace Incoders.Hive.Infrastructure.Integrations;

public sealed class DataProtectionIntegrationSecretProtector : IIntegrationSecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionIntegrationSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("incoders.hive.integrations.credentials.v1");
    }

    public string Protect(string value) => _protector.Protect(value);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}