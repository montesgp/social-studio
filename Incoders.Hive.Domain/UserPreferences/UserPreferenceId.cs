namespace Incoders.Hive.Domain.UserPreferences;

public readonly record struct UserPreferenceId(Guid Value)
{
    public static UserPreferenceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
