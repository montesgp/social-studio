namespace SocialStudio.Application.Abstractions;

/// <summary>
/// Indirection over <see cref="Task.Delay(TimeSpan, CancellationToken)"/> so poll-and-wait Application
/// services (e.g. <c>ContentWorkflowEnqueueSequencer</c>) stay unit-testable without real wall-clock
/// waits — tests fake this alongside <see cref="IClock"/> to advance time deterministically.
/// </summary>
public interface IAsyncDelay
{
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken = default);
}
