namespace Incoders.Hive.Domain.Integrations;

public enum IntegrationConnectionMode
{
    BringYourOwnKey = 0,
    ManagedByIncoders = 1,
    SystemDefault = 2,
}