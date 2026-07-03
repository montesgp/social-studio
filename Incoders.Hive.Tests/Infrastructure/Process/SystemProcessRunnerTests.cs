using Incoders.Hive.Infrastructure.Process;

namespace Incoders.Hive.Tests.Infrastructure.Process;

public sealed class SystemProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_UsesArgumentListWithoutShellExpansion_AndCapturesStdout()
    {
        var runner = new SystemProcessRunner();
        var result = await runner.RunAsync(new ProcessRunRequest(
            FileName: "dotnet",
            Arguments: ["--info"],
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Timeout: TimeSpan.FromSeconds(30)), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(".NET", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_DoesNotUseShell_WhenArgumentContainsShellOperators()
    {
        var runner = new SystemProcessRunner();
        var marker = Path.Combine(Path.GetTempPath(), $"shell-injection-{Guid.NewGuid():N}.txt");

        var result = await runner.RunAsync(new ProcessRunRequest(
            FileName: "dotnet",
            Arguments: ["--info", $"& echo hacked > {marker}"],
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Timeout: TimeSpan.FromSeconds(30)), CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task RunAsync_TimeoutKillsProcess_AndMarksTimedOut()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(new ProcessRunRequest(
            FileName: "dotnet",
            Arguments: ["exec", "definitely-missing.dll"],
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Timeout: TimeSpan.FromMilliseconds(1)), CancellationToken.None);

        Assert.True(result.TimedOut || result.ExitCode != 0);
    }


    [Fact]
    public async Task RunAsync_CallerCancellationAfterStart_KillsProcessTree()
    {
        var runner = new SystemProcessRunner();
        var started = Path.Combine(Path.GetTempPath(), $"claude-p-started-{Guid.NewGuid():N}.txt");
        var completed = Path.Combine(Path.GetTempPath(), $"claude-p-completed-{Guid.NewGuid():N}.txt");
        using var cts = new CancellationTokenSource();

        var runTask = runner.RunAsync(new ProcessRunRequest(
            FileName: "pwsh",
            Arguments: ["-NoProfile", "-Command", $"Set-Content -LiteralPath '{started}' -Value started; Start-Sleep -Seconds 2; Set-Content -LiteralPath '{completed}' -Value completed"],
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Timeout: TimeSpan.FromSeconds(30)), cts.Token);

        await WaitForFileAsync(started);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.False(File.Exists(completed));
    }

    [Fact]
    public async Task RunAsync_StopsReadingStdout_WhenConfiguredLimitIsExceeded()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(new ProcessRunRequest(
            FileName: "pwsh",
            Arguments: ["-NoProfile", "-Command", "Write-Output 'abcdefghijklmnopqrstuvwxyz'"],
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Timeout: TimeSpan.FromSeconds(30),
            MaxStandardOutputBytes: 8), CancellationToken.None);

        Assert.True(result.OutputLimitExceeded);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task RunAsync_StopsReadingStderr_WhenConfiguredLimitIsExceeded()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(new ProcessRunRequest(
            FileName: "pwsh",
            Arguments: ["-NoProfile", "-Command", "[Console]::Error.WriteLine('abcdefghijklmnopqrstuvwxyz')"],
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Timeout: TimeSpan.FromSeconds(30),
            MaxStandardErrorBytes: 8), CancellationToken.None);

        Assert.True(result.OutputLimitExceeded);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task RunAsync_DecodesChildStdoutAsUtf8_NoMojibake()
    {
        var runner = new SystemProcessRunner();

        // Child emits UTF-8 bytes for non-ASCII Spanish copy. The runner must decode them as UTF-8;
        // on Windows the redirected-stream default (OEM codepage) would corrupt this into mojibake.
        var result = await runner.RunAsync(new ProcessRunRequest(
            FileName: "pwsh",
            Arguments: ["-NoProfile", "-Command", "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Write-Output '¿qué elegís?'"],
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Timeout: TimeSpan.FromSeconds(30)), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("¿qué elegís?", result.StandardOutput);
    }

    private static async Task WaitForFileAsync(string path)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(File.Exists(path), $"Expected process marker file '{path}' to be created.");
    }

    [Fact]
    public async Task RunAsync_CapturesStderr()
    {
        var runner = new SystemProcessRunner();

        var result = await runner.RunAsync(new ProcessRunRequest(
            FileName: "dotnet",
            Arguments: ["exec", "definitely-missing.dll"],
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Timeout: TimeSpan.FromSeconds(30)), CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }
}
