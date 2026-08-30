using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.NicheDiscovery;

/// <summary>
/// Operator-chosen analysis window, in days (ND-01). Valid range 7..365, default 90.
/// </summary>
public readonly record struct DiscoveryWindow
{
    public const int MinDays = 7;
    public const int MaxDays = 365;
    public const int DefaultDays = 90;

    private DiscoveryWindow(int days)
    {
        Days = days;
    }

    public int Days { get; }

    public static DiscoveryWindow Default() => new(DefaultDays);

    public static Result<DiscoveryWindow> Create(int days)
    {
        if (days < MinDays || days > MaxDays)
        {
            return Result.Failure<DiscoveryWindow>(NicheDiscoveryErrors.WindowOutOfRange);
        }

        return Result.Success(new DiscoveryWindow(days));
    }
}
