namespace Incoders.Hive.Domain.Social.ContentStudio;

public readonly record struct ContentSetId(Guid Value)
{
    public static ContentSetId New() => new(Guid.NewGuid());
}
