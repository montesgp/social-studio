namespace Incoders.Hive.Infrastructure.Process;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default);
}

public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    TimeSpan Timeout,
    IReadOnlyDictionary<string, string>? Environment = null,
    int MaxStandardOutputBytes = 1_048_576,
    int MaxStandardErrorBytes = 262_144,
    // When set, written to the child process's stdin (UTF-8) then closed. Use this instead of
    // passing large payloads as command-line arguments — on Windows the command line is capped at
    // ~32 KB, so a big argument fails to even start the process (Win32 206). See issue #69.
    string? StandardInput = null);

public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool OutputLimitExceeded = false);
