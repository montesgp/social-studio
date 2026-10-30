using SocialStudio.Application.Abstractions;

namespace SocialStudio.Tests.Common;

internal sealed class TestClock : IClock
{
    public TestClock(DateTime utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; set; }
}
