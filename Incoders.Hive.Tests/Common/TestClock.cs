using Incoders.Hive.Application.Abstractions;

namespace Incoders.Hive.Tests.Common;

internal sealed class TestClock : IClock
{
    public TestClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; set; }
}
