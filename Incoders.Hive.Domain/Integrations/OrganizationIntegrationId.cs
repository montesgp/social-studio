namespace Incoders.Hive.Domain.Integrations;

public readonly record struct OrganizationIntegrationId(Guid Value)
{
    public static OrganizationIntegrationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}