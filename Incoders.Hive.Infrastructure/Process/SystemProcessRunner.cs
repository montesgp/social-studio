using System.Diagnostics;
using System.Text;

namespace Incoders.Hive.Infrastructure.Process;

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("Process file name is required.", nameof(request));
        }

        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var (key, value) in request.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();

        var stdoutTask = CaptureOutputAsync(process.StandardOutput, request.MaxStandardOutputBytes, linkedCts.Token);
        var stderrTask = CaptureOutputAsync(process.StandardError, request.MaxStandardErrorBytes, linkedCts.Token);
        var waitTask = process.WaitForExitAsync(linkedCts.Token);
        var timedOut = false;

        try
        {
            await AwaitProcessAndOutputAsync(waitTask, stdoutTask, stderrTask);
            return new ProcessRunResult(process.ExitCode, await stdoutTask, await stderrTask, TimedOut: false);
        }
        catch (OutputLimitExceededException)
        {
            KillProcessTree(process);
            return new ProcessRunResult(-1, await ReadBestEffortAsync(stdoutTask), await ReadBestEffortAsync(stderrTask), TimedOut: false, OutputLimitExceeded: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            timedOut = true;
            KillProcessTree(process);
        }

        var capturedStdout = await ReadBestEffortAsync(stdoutTask);
        var capturedStderr = await ReadBestEffortAsync(stderrTask);
        return new ProcessRunResult(-1, capturedStdout, capturedStderr, timedOut);
    }

    private static void KillProcessTree(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task AwaitProcessAndOutputAsync(Task waitTask, Task<string> stdoutTask, Task<string> stderrTask)
    {
        var pending = new HashSet<Task> { waitTask, stdoutTask, stderrTask };
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            await completed;
        }
    }

    private static async Task<string> CaptureOutputAsync(StreamReader reader, int maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Output byte limit must be positive.");
        }

        var builder = new StringBuilder();
        var buffer = new char[1024];
        var capturedBytes = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                return builder.ToString();
            }

            capturedBytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
            if (capturedBytes > maxBytes)
            {
                throw new OutputLimitExceededException();
            }

            builder.Append(buffer, 0, read);
        }
    }

    private static async Task<string> ReadBestEffortAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (OutputLimitExceededException)
        {
            return string.Empty;
        }
    }

    private sealed class OutputLimitExceededException : Exception;
}
