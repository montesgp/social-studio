namespace SocialStudio.Domain.Integrations;

/// <summary>
/// Strongly-typed identifier for <see cref="CanvaUserToken"/>.
/// </summary>
public readonly record struct CanvaUserTokenId(Guid Value)
{
    public static CanvaUserTokenId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
