using System.Globalization;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

/// <summary>
/// Per-org configuration for the <c>comfyui</c> connector (OQ-4). Binding mirrors
/// <see cref="ClaudePromptCliSettings.FromCredential"/>: read credential keys with safe
/// defaults, validate the base URL and positive-int fields, else
/// <see cref="IntegrationErrors.ProviderCredentialUnavailable"/>.
/// Refs: CUI-03, CUI-03-a, CUI-03-b; design §3.1, §0 OQ-4.
/// </summary>
public sealed record ComfyUiSettings(
    string BaseUrl,
    string? DefaultCheckpoint,
    string? IpAdapterModel,
    /// <summary>
    /// API-format ComfyUI workflow graph with <c>{{TOKEN}}</c> placeholders. Null/empty signals
    /// the bundled <c>DefaultComfyUiWorkflowTemplate.json</c> resource should be used instead.
    /// </summary>
    string? WorkflowTemplateJson,
    /// <summary>Node id whose <c>/history</c> output carries <c>images[]</c> (default "9").</summary>
    string OutputNodeId,
    /// <summary>Cap on resolved reference images -&gt; <c>Capabilities.MaxConditioningImages</c>.</summary>
    int MaxReferenceImages,
    TimeSpan Timeout,
    TimeSpan PollInterval,
    /// <summary>Curated checkpoint fallback used by the catalog client when <c>/object_info</c> is unreachable.</summary>
    IReadOnlyList<string> DefaultCheckpoints,
    /// <summary>
    /// KSampler step count. Lower values iterate faster but degrade image quality — verified live
    /// on the reference DirectML backend: 20 steps / CFG 7.0 produced a properly textured
    /// photographic background, 6 steps / CFG 2.0 collapsed to a flat untextured fill. Intended for
    /// local/dev tuning only; production installs should keep <see cref="DefaultSteps"/>.
    /// </summary>
    int Steps,
    /// <summary>
    /// KSampler classifier-free guidance scale. Same local/dev-only tuning caveat as
    /// <see cref="Steps"/> — see the field doc above.
    /// </summary>
    double Cfg)
{
    public const string ProviderKey = "comfyui";
    public const string DefaultBaseUrl = "http://127.0.0.1:8188";
    public const string DefaultOutputNodeId = "9";
    public const int DefaultMaxReferenceImages = 1;

    /// <summary>Default KSampler step count. MUST NOT change — verified quality baseline (design §0 OQ-6).</summary>
    public const int DefaultSteps = 20;

    /// <summary>Default KSampler CFG scale. MUST NOT change — verified quality baseline (design §0 OQ-6).</summary>
    public const double DefaultCfg = 7.0;

    /// <summary>Sane upper bound for the CFG scale credential override; ComfyUI's own KSampler slider tops out here.</summary>
    public const double MaxCfg = 30.0;

    // Overall generation budget. Sized for SDXL on non-CUDA hardware (e.g. DirectML/DML),
    // where a single image can take ~6 min (~15 s/iteration x 20 steps plus model/VAE load).
    // A lower value silently times out and degrades every slide to CSS; override per-org via
    // the credential's timeout_ms when running on faster (CUDA) hardware.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(600_000);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(1_000);

    /// <summary>
    /// Curated set of commercial-OK checkpoints, used as the catalog fallback when
    /// <c>/object_info</c> is unreachable so "Sync models" never errors (CUI-04-c).
    /// </summary>
    public static readonly IReadOnlyList<string> CuratedDefaultCheckpoints =
    [
        "v1-5-pruned-emaonly.safetensors",
        "sd_xl_base_1.0.safetensors",
    ];

    public static Result<ComfyUiSettings> FromCredential(ResolvedProviderCredential credential)
    {
        var baseUrlResult = ParseBaseUrl(Read(credential, "base_url"));
        if (baseUrlResult.IsFailure)
        {
            return Result.Failure<ComfyUiSettings>(baseUrlResult.Error);
        }

        var timeoutMsResult = ParsePositiveInt(Read(credential, "timeout_ms"), (int)DefaultTimeout.TotalMilliseconds);
        if (timeoutMsResult.IsFailure)
        {
            return Result.Failure<ComfyUiSettings>(timeoutMsResult.Error);
        }

        var pollIntervalMsResult = ParsePositiveInt(Read(credential, "poll_interval_ms"), (int)DefaultPollInterval.TotalMilliseconds);
        if (pollIntervalMsResult.IsFailure)
        {
            return Result.Failure<ComfyUiSettings>(pollIntervalMsResult.Error);
        }

        var maxReferenceImagesResult = ParsePositiveInt(Read(credential, "max_reference_images"), DefaultMaxReferenceImages);
        if (maxReferenceImagesResult.IsFailure)
        {
            return Result.Failure<ComfyUiSettings>(maxReferenceImagesResult.Error);
        }

        var stepsResult = ParsePositiveInt(Read(credential, "steps"), DefaultSteps);
        if (stepsResult.IsFailure)
        {
            return Result.Failure<ComfyUiSettings>(stepsResult.Error);
        }

        var cfgResult = ParseCfg(Read(credential, "cfg"), DefaultCfg);
        if (cfgResult.IsFailure)
        {
            return Result.Failure<ComfyUiSettings>(cfgResult.Error);
        }

        var workflowTemplateJson = Read(credential, "workflow_template_json");

        return Result.Success(new ComfyUiSettings(
            baseUrlResult.Value,
            Read(credential, "default_checkpoint"),
            Read(credential, "ipadapter_model"),
            string.IsNullOrWhiteSpace(workflowTemplateJson) ? null : workflowTemplateJson,
            Read(credential, "output_node_id") ?? DefaultOutputNodeId,
            maxReferenceImagesResult.Value,
            TimeSpan.FromMilliseconds(timeoutMsResult.Value),
            TimeSpan.FromMilliseconds(pollIntervalMsResult.Value),
            CuratedDefaultCheckpoints,
            stepsResult.Value,
            cfgResult.Value));
    }

    private static Result<string> ParseBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success(DefaultBaseUrl);
        }

        var trimmed = value.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? Result.Success(trimmed)
                : Result.Failure<string>(IntegrationErrors.ProviderCredentialUnavailable);
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

    /// <summary>Positive and no larger than <see cref="MaxCfg"/> — an out-of-band value maps to
    /// <see cref="IntegrationErrors.ProviderCredentialUnavailable"/> instead of silently reaching
    /// KSampler with a nonsensical guidance scale.</summary>
    private static Result<double> ParseCfg(string? value, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success(defaultValue);
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0
            && parsed <= MaxCfg
                ? Result.Success(parsed)
                : Result.Failure<double>(IntegrationErrors.ProviderCredentialUnavailable);
    }

    private static string? Read(ResolvedProviderCredential credential, string key) =>
        ProviderCredentialReader.TryGet(credential, key, out var value) ? value.Trim() : null;
}
