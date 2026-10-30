using SocialStudio.Application.Abstractions;

namespace SocialStudio.Infrastructure.Time;

/// <summary>
/// Default <see cref="IAsyncDelay"/> backed by <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
/// </summary>
public sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default) =>
        Task.Delay(duration, cancellationToken);
}
