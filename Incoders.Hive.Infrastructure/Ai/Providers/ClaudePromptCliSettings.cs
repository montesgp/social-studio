using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

public sealed record ClaudePromptCliSettings(
    string ExecutablePath,
    string? WorkingDirectory,
    string? AllowedOutputDirectory,
    string? DefaultModel,
    TimeSpan Timeout,
    string OutputFormat,
    string? JsonSchema,
    string? PermissionMode,
    IReadOnlyList<string> AllowedTools,
    string? SettingsJson,
    int MaxImageFileBytes)
{
    public const string ProviderKey = "claude-p";
    public const string DefaultExecutable = "claude";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);
    public const int DefaultMaxImageFileBytes = 10 * 1024 * 1024;

    private static readonly string[] AllowedExecutables = ["claude", "claude.exe", "claude.cmd"];
    private static readonly string[] AllowedOutputFormats = ["text", "json"];
    private static readonly char[] UnsafeCommandChars = ['&', '|', ';', '<', '>', '`', '\n', '\r'];

    public static Result<ClaudePromptCliSettings> FromCredential(ResolvedProviderCredential credential)
    {
        var executable = Read(credential, "executable_path") ?? DefaultExecutable;
        if (IsUnsafeExecutable(executable))
        {
            return Result.Failure<ClaudePromptCliSettings>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var workingDirectory = Read(credential, "working_directory");
        if (!IsExistingDirectoryOrNull(workingDirectory))
        {
            return Result.Failure<ClaudePromptCliSettings>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var allowedOutputDirectory = Read(credential, "allowed_output_directory");
        if (!IsExistingDirectoryOrNull(allowedOutputDirectory))
        {
            return Result.Failure<ClaudePromptCliSettings>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var timeoutResult = ParseTimeout(Read(credential, "timeout_ms"));
        if (timeoutResult.IsFailure)
        {
            return Result.Failure<ClaudePromptCliSettings>(timeoutResult.Error);
        }

        var maxImageFileBytesResult = ParsePositiveInt(Read(credential, "max_image_file_bytes"), DefaultMaxImageFileBytes);
        if (maxImageFileBytesResult.IsFailure)
        {
            return Result.Failure<ClaudePromptCliSettings>(maxImageFileBytesResult.Error);
        }

        var outputFormat = Read(credential, "output_format") ?? "text";
        if (!AllowedOutputFormats.Contains(outputFormat, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<ClaudePromptCliSettings>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var tools = SplitCsv(Read(credential, "allowed_tools"));
        if (tools.Any(IsUnsafeToken))
        {
            return Result.Failure<ClaudePromptCliSettings>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        var permissionMode = Read(credential, "permission_mode");
        if (permissionMode is not null && IsUnsafeToken(permissionMode))
        {
            return Result.Failure<ClaudePromptCliSettings>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        return Result.Success(new ClaudePromptCliSettings(
            executable.Trim(),
            NormalizeDirectory(workingDirectory),
            NormalizeDirectory(allowedOutputDirectory),
            Read(credential, "default_model"),
            timeoutResult.Value,
            outputFormat.Trim().ToLowerInvariant(),
            Read(credential, "json_schema"),
            permissionMode,
            tools,
            Read(credential, "settings_json"),
            maxImageFileBytesResult.Value));
    }

    public string ResolveModel(string? preferredModel) =>
        string.IsNullOrWhiteSpace(preferredModel)
            ? DefaultModel ?? string.Empty
            : preferredModel.Trim();

    private static string? Read(ResolvedProviderCredential credential, string key) =>
        ProviderCredentialReader.TryGet(credential, key, out var value) ? value.Trim() : null;

    private static Result<TimeSpan> ParseTimeout(string? value)
    {
        var parsed = ParsePositiveInt(value, (int)DefaultTimeout.TotalMilliseconds);
        return parsed.IsFailure
            ? Result.Failure<TimeSpan>(IntegrationErrors.RoutingCandidateTimeoutInvalid)
            : Result.Success(TimeSpan.FromMilliseconds(parsed.Value));
    }

    private static Result<int> ParsePositiveInt(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success(defaultValue);
        }

        return int.TryParse(value, out var parsed) && parsed > 0
            ? Result.Success(parsed)
            : Result.Failure<int>(IntegrationErrors.ProviderCredentialUnavailable);
    }

    private static bool IsUnsafeExecutable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(UnsafeCommandChars) >= 0)
        {
            return true;
        }

        var normalized = executable.Trim();
        if (normalized.Contains(" /", StringComparison.Ordinal)
            || normalized.Contains(" -", StringComparison.Ordinal)
            || normalized.StartsWith("cmd ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("powershell ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("pwsh ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var basename = Path.GetFileName(normalized);
        return !AllowedExecutables.Contains(basename, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsUnsafeToken(string token) =>
        string.IsNullOrWhiteSpace(token) || token.IndexOfAny(UnsafeCommandChars) >= 0;

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsExistingDirectoryOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) || Directory.Exists(value);

    private static string? NormalizeDirectory(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value.Trim());
}
