namespace Incoders.Hive.Domain.Integrations;

public readonly record struct OrganizationIntegrationCredentialId(Guid Value)
{
    public static OrganizationIntegrationCredentialId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}