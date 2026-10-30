namespace SocialStudio.Domain.Integrations;

public readonly record struct IntegrationProviderId(Guid Value)
{
    public static IntegrationProviderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}