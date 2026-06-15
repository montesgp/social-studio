namespace Incoders.Hive.Domain.Social.ContentStudio;

public readonly record struct ContentItemId(Guid Value)
{
    public static ContentItemId New() => new(Guid.NewGuid());
}
