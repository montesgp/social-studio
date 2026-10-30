using System.Diagnostics;
using System.Text;

namespace SocialStudio.Infrastructure.Process;

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
            // Force UTF-8 when decoding child output. On Windows the redirected-stream default is the
            // console OEM codepage (CP437/850), which corrupts non-ASCII UTF-8 output into mojibake
            // (e.g. "¿qué elegís?" -> "Ôö¼ÔöÉquÔö£┬«"). Claude Code emits UTF-8; decode it as UTF-8 on
            // every platform (no-op on Linux, where UTF-8 is already the default).
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var hasStandardInput = request.StandardInput is not null;
        if (hasStandardInput)
        {
            startInfo.RedirectStandardInput = true;
            startInfo.StandardInputEncoding = Encoding.UTF8;
        }

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

        // Start draining stdout/stderr BEFORE writing stdin. Writing a large stdin payload first would
        // deadlock: the child blocks writing stdout (its pipe buffer full, nobody reading) while we
        // block writing stdin (the child's stdin buffer full because it stopped reading to flush stdout).
        // Small payloads fit the OS pipe buffer and hid this; a large prompt hangs until timeout. (#69)
        var stdoutTask = CaptureOutputAsync(process.StandardOutput, request.MaxStandardOutputBytes, linkedCts.Token);
        var stderrTask = CaptureOutputAsync(process.StandardError, request.MaxStandardErrorBytes, linkedCts.Token);
        var stdinTask = hasStandardInput
            ? WriteStandardInputAsync(process, request.StandardInput!, linkedCts.Token)
            : Task.CompletedTask;
        var waitTask = process.WaitForExitAsync(linkedCts.Token);
        var timedOut = false;

        try
        {
            await AwaitProcessAndOutputAsync(waitTask, stdoutTask, stderrTask, stdinTask);
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

    private static async Task WriteStandardInputAsync(System.Diagnostics.Process process, string input, CancellationToken ct)
    {
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), ct);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Broken pipe: the child exited before consuming all stdin. Benign — never mask stdout/stderr.
        }
    }

    private static async Task AwaitProcessAndOutputAsync(Task waitTask, Task<string> stdoutTask, Task<string> stderrTask, Task stdinTask)
    {
        var pending = new HashSet<Task> { waitTask, stdoutTask, stderrTask, stdinTask };
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
