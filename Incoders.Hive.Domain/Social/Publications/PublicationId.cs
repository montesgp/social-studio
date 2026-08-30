namespace Incoders.Hive.Domain.Social.Publications;

public readonly record struct PublicationId(Guid Value)
{
    public static PublicationId New() => new(Guid.NewGuid());
}
