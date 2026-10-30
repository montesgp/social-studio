using System.Text;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;
using SocialStudio.Infrastructure.Process;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Infrastructure.Ai;

/// <summary>
/// Vision input for the claude-p CLI provider.
///
/// <para>
/// The CLI has no request body to carry base64 image blocks the way the HTTP API adapter does
/// (<c>ClaudeModelClient.BuildMultimodalContent</c>), so <see cref="AiCompletionRequest.Images"/>
/// used to be dropped on the floor: the model answered from captions alone and hallucinated a
/// plausible-but-wrong brand palette. The CLI CAN see an image when it is given a filesystem path
/// and the <c>Read</c> tool — verified against `claude -p --allowedTools Read` on 2026-08-21, both
/// inside and outside the working directory.
/// </para>
///
/// <para>
/// So the adapter stages each image to a per-invocation directory, lists the absolute paths in the
/// prompt, grants <c>Read</c>, and deletes the directory afterwards.
/// </para>
/// </summary>
public sealed class ClaudePromptCliVisionTests
{
    [Fact]
    public async Task CompleteAsync_WithImages_StagesThemToDiskAndListsPathsInThePrompt()
    {
        using var staging = new TempDirectory();
        var runner = new StagingAwareProcessRunner(new ProcessRunResult(0, "{\"ok\":true}", "", false));

        var client = BuildClient(runner, staging.Path);

        var result = await client.CompleteAsync(
            BuildRequest("describe the brand", [
                new AiImageInput("image/jpeg", Base64Of([1, 2, 3, 4])),
                new AiImageInput("image/png", Base64Of([5, 6, 7, 8])),
            ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Two files existed on disk at the moment the CLI was invoked.
        Assert.Equal(2, runner.StagedFilesAtRunTime.Count);

        // Extensions follow the declared media type so the CLI recognises the format.
        Assert.Contains(runner.StagedFilesAtRunTime, p => p.EndsWith(".jpg", StringComparison.Ordinal));
        Assert.Contains(runner.StagedFilesAtRunTime, p => p.EndsWith(".png", StringComparison.Ordinal));

        // Bytes round-trip intact.
        var jpg = runner.StagedFilesAtRunTime.Single(p => p.EndsWith(".jpg", StringComparison.Ordinal));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, runner.StagedBytesAtRunTime[jpg]);

        // Every staged path is quoted verbatim in the stdin prompt, and the caller's own prompt survives.
        var stdin = runner.Request!.StandardInput!;
        foreach (var path in runner.StagedFilesAtRunTime)
        {
            Assert.Contains(path, stdin, StringComparison.Ordinal);
        }

        Assert.Contains("describe the brand", stdin, StringComparison.Ordinal);
        Assert.Contains("Read", stdin, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_WithImages_GrantsTheReadToolSoTheCliCanOpenThem()
    {
        using var staging = new TempDirectory();
        var runner = new StagingAwareProcessRunner(new ProcessRunResult(0, "{}", "", false));
        var client = BuildClient(runner, staging.Path);

        var result = await client.CompleteAsync(
            BuildRequest("x", [new AiImageInput("image/jpeg", Base64Of([9]))]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var args = runner.Request!.Arguments;
        var readGrants = args
            .Select((value, index) => (value, index))
            .Where(x => string.Equals(x.value, "--allowedTools", StringComparison.Ordinal))
            .Select(x => args[x.index + 1])
            .ToList();

        Assert.Contains("Read", readGrants);
    }

    [Fact]
    public async Task CompleteAsync_DeletesStagedImagesAfterTheCallEvenWhenTheCliFails()
    {
        using var staging = new TempDirectory();
        var runner = new StagingAwareProcessRunner(new ProcessRunResult(1, "", "boom", false));
        var client = BuildClient(runner, staging.Path);

        var result = await client.CompleteAsync(
            BuildRequest("x", [new AiImageInput("image/jpeg", Base64Of([9]))]),
            CancellationToken.None);

        Assert.True(result.IsFailure);

        // Files existed during the call...
        Assert.Single(runner.StagedFilesAtRunTime);
        // ...and nothing is left behind afterwards.
        Assert.Empty(Directory.EnumerateFileSystemEntries(staging.Path));
    }

    [Fact]
    public async Task CompleteAsync_WithoutImages_IsByteIdenticalToTheTextOnlyPath()
    {
        using var staging = new TempDirectory();
        var runner = new StagingAwareProcessRunner(new ProcessRunResult(0, "{}", "", false));
        var client = BuildClient(runner, staging.Path);

        var result = await client.CompleteAsync(BuildRequest("hello", images: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // No preamble, no staging, no Read grant — unchanged behaviour for every existing caller.
        Assert.Equal("hello", runner.Request!.StandardInput);
        Assert.DoesNotContain("Read", runner.Request.Arguments);
        Assert.Empty(runner.StagedFilesAtRunTime);
    }

    [Fact]
    public async Task CompleteAsync_IgnoresImagesThatCarryNoInlineBytes()
    {
        using var staging = new TempDirectory();
        var runner = new StagingAwareProcessRunner(new ProcessRunResult(0, "{}", "", false));
        var client = BuildClient(runner, staging.Path);

        // Url-only inputs cannot be staged (the CLI has no fetcher); they degrade to text-only
        // rather than failing the completion.
        var result = await client.CompleteAsync(
            BuildRequest("hello", [new AiImageInput("image/jpeg", null, "https://example.test/a.jpg")]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", runner.Request!.StandardInput);
        Assert.Empty(runner.StagedFilesAtRunTime);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string Base64Of(byte[] bytes) => Convert.ToBase64String(bytes);

    private static ClaudePromptCliModelClient BuildClient(IProcessRunner runner, string stagingRoot) =>
        new(
            new ResolvedProviderCredential(
                Guid.Empty,
                "claude-p",
                IntegrationConnectionMode.BringYourOwnKey,
                new Dictionary<string, string> { ["executable_path"] = "claude" },
                "claude-default"),
            runner,
            NullLogger<ClaudePromptCliModelClient>.Instance,
            stagingRoot);

    private static AiCompletionRequest BuildRequest(string input, IReadOnlyList<AiImageInput>? images) =>
        new(Guid.NewGuid(), "ai-aesthetic", "aesthetic-extractor/v4", input, "claude-p", "claude-opus-4-8",
            OutputJsonSchema: null, Images: images);

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hive-vision-tests-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort test cleanup.
            }
        }
    }

    /// <summary>
    /// Snapshots what is on disk at the moment the CLI would run, because the adapter deletes the
    /// staging directory before returning.
    /// </summary>
    private sealed class StagingAwareProcessRunner : IProcessRunner
    {
        private readonly ProcessRunResult _result;

        public StagingAwareProcessRunner(ProcessRunResult result) => _result = result;

        public ProcessRunRequest? Request { get; private set; }

        public List<string> StagedFilesAtRunTime { get; } = [];

        public Dictionary<string, byte[]> StagedBytesAtRunTime { get; } = [];

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;

            var stdin = request.StandardInput ?? string.Empty;
            foreach (var path in ExtractExistingPaths(stdin))
            {
                StagedFilesAtRunTime.Add(path);
                StagedBytesAtRunTime[path] = File.ReadAllBytes(path);
            }

            return Task.FromResult(_result);
        }

        /// <summary>Staged paths are emitted one per line as "- &lt;absolute path&gt;".</summary>
        private static IEnumerable<string> ExtractExistingPaths(string stdin) =>
            stdin
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
                .Select(line => line[2..].Trim())
                .Where(File.Exists);
    }
}
