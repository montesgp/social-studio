using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Ai.Providers;

/// <summary>
/// Infrastructure adapter: local ComfyUI server driven over HTTP with a templated IP-Adapter
/// workflow. Implements <see cref="IImageModelClient"/> — the only place where ComfyUI
/// HTTP/JSON-graph types exist.
///
/// <para>
/// Architecture gate: ALL ComfyUI-specific types, HTTP concerns, and JSON-graph parsing are
/// CONFINED to this file. No ComfyUI type escapes into Application or Domain.
/// </para>
///
/// <para>
/// OQ-1: the workflow graph is stored as an API-format JSON template string with named
/// <c>{{TOKEN}}</c> placeholders (<see cref="SubstituteTemplate"/>); a bundled default ships as
/// <c>DefaultComfyUiWorkflowTemplate.json</c> (embedded resource) for when
/// <see cref="ComfyUiSettings.WorkflowTemplateJson"/> is null/empty.
/// </para>
///
/// Refs: CUI-01, CUI-01-a, CUI-01-b, CUI-01-c, CUI-IMG-01; design §3.2, §3.3, §3.4, §0 OQ-1/OQ-2.
/// </summary>
public sealed class ComfyUiImageModelClient : IImageModelClient
{
    private const string ProviderKey = "comfyui";
    private const string DefaultImageMime = "image/png";
    private const string EmbeddedTemplateResourceName =
        "Incoders.Hive.Infrastructure.Ai.Providers.DefaultComfyUiWorkflowTemplate.json";
    private const string EmbeddedDetailerTemplateResourceName =
        "Incoders.Hive.Infrastructure.Ai.Providers.DefaultComfyUiWorkflowTemplateWithDetailer.json";

    /// <summary>
    /// Default negative prompt. The generated image is used ONLY as a background raster; any text it
    /// bakes in is not editable in the studio and clashes with the separate editable text layer. The
    /// reference images conditioning the IP-Adapter are finished posts that contain text, so without
    /// this the model reproduces garbled pseudo-text. Suppress all typography plus common artifacts,
    /// and (WU-4, CAGH-CUI-02) anatomy-defect terms — the background is composited behind a subject
    /// on the finished post and malformed limbs/hands/faces are otherwise a common IP-Adapter defect.
    /// </summary>
    private const string DefaultNegativePrompt =
        "text, letters, words, typography, watermark, signature, caption, logo, label, subtitles, " +
        "writing, font, handwriting, numbers, low quality, blurry, deformed, distorted, extra limbs, " +
        "extra fingers, missing fingers, fused fingers, bad hands, malformed hands, deformed face, " +
        "duplicate face, cloned face, two heads, extra person, mutated, disfigured, bad anatomy";

    /// <summary>
    /// Exact JSON substring for the KSampler "model" edge wired to the IP-Adapter output (node
    /// "6"). When no reference image resolves, this edge is replaced by
    /// <see cref="PassThroughModelEdge"/> so the graph bypasses the LoadImage/IP-Adapter chain
    /// entirely (design §3.3 no-reference bypass).
    /// </summary>
    private const string ConditionedModelEdge = "\"model\": [\"6\", 0]";

    /// <summary>Pass-through model edge wired directly to the checkpoint loader (node "1").</summary>
    private const string PassThroughModelEdge = "\"model\": [\"1\", 0]";

    /// <summary>
    /// Budget for releasing an abandoned prompt on the backend. Short by design: the generation has
    /// already failed and the caller is waiting to retry, so cleanup must not extend the outage it
    /// is trying to end.
    /// </summary>
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<ComfyUiImageModelClient> _logger;

    public ComfyUiImageModelClient(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        IGeneratedAssetStorage storage,
        ILogger<ComfyUiImageModelClient>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _logger = logger ?? NullLogger<ComfyUiImageModelClient>.Instance;
    }

    /// <summary>
    /// ComfyUI supports IP-Adapter image conditioning (CUI-IMG-01). Declarative only — never
    /// triggers a provider call; falls back to <see cref="ComfyUiSettings.DefaultMaxReferenceImages"/>
    /// when the credential cannot be parsed.
    /// </summary>
    public ImageModelCapabilities Capabilities
    {
        get
        {
            var settingsResult = ComfyUiSettings.FromCredential(_credential);
            var maxConditioningImages = settingsResult.IsSuccess
                ? settingsResult.Value.MaxReferenceImages
                : ComfyUiSettings.DefaultMaxReferenceImages;

            return new ImageModelCapabilities(SupportsImageConditioning: true, MaxConditioningImages: maxConditioningImages);
        }
    }

    /// <summary>
    /// OQ-2 HTTP call sequence: <c>FromCredential -&gt; resolve checkpoint -&gt; resolve reference
    /// bytes -&gt; upload -&gt; substitute template -&gt; POST /prompt -&gt; poll GET /history/{id} -&gt;
    /// GET /view -&gt; bytes</c>. Refs: CUI-01-a, CUI-01-b, CUI-01-c; design §3.4 steps 0-5.
    /// </summary>
    public async Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken ct = default)
    {
        var settingsResult = ComfyUiSettings.FromCredential(_credential);
        if (settingsResult.IsFailure)
        {
            _logger.LogWarning(
                "ComfyUiImageModelClient: invalid settings. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(settingsResult.Error);
        }

        var settings = settingsResult.Value;

        var checkpoint = request.PreferredModel ?? _credential.SelectedModel ?? settings.DefaultCheckpoint;
        if (string.IsNullOrWhiteSpace(checkpoint))
        {
            _logger.LogWarning(
                "ComfyUiImageModelClient: checkpoint missing. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.AiModelRequired);
        }

        var baseUrl = settings.BaseUrl.TrimEnd('/');

        // WU-3 (CUI-02-c): bound the ENTIRE HTTP flow (upload/queue/poll/fetch) by the
        // configured overall Timeout, in addition to the caller-supplied CancellationToken.
        // Both an elapsed budget and a caller-cancelled token map onto ProviderTimeout below.
        using var timeoutCts = new CancellationTokenSource(settings.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        // Tracked outside the try so the cancellation handler knows whether this call left work
        // outstanding on the backend. Set only once POST /prompt is acknowledged.
        string? queuedPromptId = null;

        try
        {
            using var http = _httpClientFactory.CreateClient(nameof(ComfyUiImageModelClient));

            var resolvedRefs = await ResolveReferenceImageBytesAsync(request, settings, effectiveCt);

            // OQ-5: upload every resolved reference (within the cap) so the ComfyUI server has
            // each byte handle available; the single-reference template (§3.3) only wires the
            // FIRST uploaded filename into the LoadImage node in v1.
            string? referenceImageName = null;
            if (resolvedRefs.Count > 0)
            {
                foreach (var resolvedRef in resolvedRefs)
                {
                    var uploadResult = await UploadReferenceImageAsync(http, baseUrl, resolvedRef, effectiveCt);
                    if (uploadResult.IsFailure)
                    {
                        return Result.Failure<ImageGenerationResult>(uploadResult.Error);
                    }

                    referenceImageName ??= uploadResult.Value;
                }
            }

            var (template, isAugmentedTemplate) = await SelectTemplateAsync(http, baseUrl, settings, effectiveCt);

            // CAGH-CUI-01: mirrors OpenAiImageModelClient's BackgroundOnly handling — prepend the
            // shared directive (ImagePromptDirectives, D5/CAGH-CUI-01-c) to the positive prompt so
            // it reaches KSampler's positive CLIPTextEncode.
            var effectivePrompt = request.BackgroundOnly
                ? $"{ImagePromptDirectives.BackgroundOnly}\n\n{request.Prompt}"
                : request.Prompt;

            var seed = Random.Shared.NextInt64(0, int.MaxValue);
            var graphResult = BuildWorkflowGraph(
                template,
                checkpoint,
                effectivePrompt,
                negativePrompt: DefaultNegativePrompt,
                referenceImageName,
                settings.IpAdapterModel,
                request.Width,
                request.Height,
                seed,
                settings.Steps,
                settings.Cfg);

            if (graphResult.IsFailure)
            {
                return Result.Failure<ImageGenerationResult>(graphResult.Error);
            }

            // A carousel generates its slides back to back against the same ComfyUI process, and the
            // backend does not reclaim cached VRAM between runs. Measured: the first image succeeds,
            // the next fails allocating as little as 209 MB, and the same request succeeds once the
            // cache is dropped. Reclaim before queueing so slide N does not inherit slide N-1's
            // residue. Best-effort by design — a backend that does not expose /free must still
            // generate.
            await TryReclaimVideoMemoryAsync(http, baseUrl, unloadModels: false, effectiveCt);

            var queueOutcome = await QueuePromptAsync(http, baseUrl, graphResult.Value, effectiveCt);
            var promptIdResult = queueOutcome.Result;
            if (promptIdResult.IsFailure)
            {
                // A5 (CUI-09-b): invalidate the cached `Supported` entry ONLY when the augmented
                // graph was the one submitted AND the rejection is detailer-attributable. An
                // unrelated ComfyUI failure (checkpoint missing, timeout, generic outage) must not
                // thrash the cache and force every subsequent job to re-probe.
                if (isAugmentedTemplate && ComfyUiDetailerProbe.IsDetailerAttributable(queueOutcome.RejectionPayload))
                {
                    ComfyUiDetailerProbe.Invalidate(baseUrl);
                }

                return Result.Failure<ImageGenerationResult>(promptIdResult.Error);
            }

            queuedPromptId = promptIdResult.Value;

            var pollOutcome = await PollHistoryAsync(http, baseUrl, promptIdResult.Value, settings, _logger, effectiveCt);
            var outputImageResult = pollOutcome.Result;
            if (outputImageResult.IsFailure)
            {
                // A real-generation gap found on a 1024 MB-VRAM local backend: a queued prompt with
                // the detailer subgraph is ACCEPTED (the node classes and detection weights are
                // genuinely present) but FaceDetailer's second KSampler pass runs out of memory
                // mid-execution — a failure that only ever surfaces here, via /history's
                // status_str == "error", never through QueuePromptAsync's rejection path above.
                // Mirror the same A5/CUI-09-b invalidation so a confirmed-fatal augmented template
                // does not keep getting re-selected (and re-OOMing) for the rest of this carousel or
                // the cache's remaining TTL.
                if (isAugmentedTemplate && ComfyUiDetailerProbe.IsDetailerAttributable(pollOutcome.ExecutionErrorDetail))
                {
                    ComfyUiDetailerProbe.Invalidate(baseUrl);
                }

                // The dominant execution failure on a memory-constrained backend is an allocation
                // failure, and the resident checkpoint is exactly what leaves no headroom. Evict it
                // so the caller's retry (IntegrationExecutionPolicy, MaxAttempts) starts clean
                // instead of hitting the same wall — the retry pays a model reload, which is far
                // cheaper than losing the slide.
                await TryReclaimVideoMemoryAsync(http, baseUrl, unloadModels: true, effectiveCt);

                return Result.Failure<ImageGenerationResult>(outputImageResult.Error);
            }

            var bytesResult = await FetchImageBytesAsync(http, baseUrl, outputImageResult.Value, effectiveCt);
            if (bytesResult.IsFailure)
            {
                return Result.Failure<ImageGenerationResult>(bytesResult.Error);
            }

            return Result.Success(new ImageGenerationResult(
                bytesResult.Value,
                DefaultImageMime,
                ProviderKey,
                checkpoint,
                new ImageUsage(ImageCount: 1, OutputBytes: bytesResult.Value.Length)));
        }
        catch (OperationCanceledException)
        {
            // CUI-02-c: never propagate. But WHICH budget ran out decides what the caller should do
            // next, and the two are not interchangeable: our own Timeout elapsing is a provider fault
            // worth retrying, while the caller withdrawing the work is not. Collapsing both onto
            // ProviderTimeout meant a cancelled job kept buying full provider attempts for every
            // slide it had left.
            var callerWithdrew = ct.IsCancellationRequested;

            _logger.LogWarning(
                "ComfyUiImageModelClient: generation {Reason}. " +
                "OrganizationId={OrganizationId} CapabilityKey={CapabilityKey} PromptId={PromptId}",
                callerWithdrew ? "was cancelled by the caller" : "exceeded its own timeout",
                request.OrganizationId,
                request.CapabilityKey,
                queuedPromptId ?? "(none)");

            // Giving up on the HTTP flow does not stop the backend: an outstanding prompt keeps
            // occupying the GPU and the caller's retry queues another one behind it. Abandon it on
            // the server too, or every subsequent attempt waits out its full budget behind work
            // nobody will collect, and the stacked prompts exhaust video memory.
            await TryAbandonQueuedPromptAsync(baseUrl, queuedPromptId);

            return Result.Failure<ImageGenerationResult>(callerWithdrew
                ? IntegrationErrors.OperationCancelled
                : IntegrationErrors.ProviderTimeout);
        }
        catch (HttpRequestException ex)
        {
            // CUI-02-a: base URL unreachable / socket-level failure.
            _logger.LogWarning(
                ex,
                "ComfyUiImageModelClient: HTTP call threw. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnavailable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Blanket safety net (CUI-02): no path may propagate an unhandled exception.
            _logger.LogWarning(
                ex,
                "ComfyUiImageModelClient: unexpected failure. OrganizationId={OrganizationId} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.CapabilityKey);

            return Result.Failure<ImageGenerationResult>(IntegrationErrors.ExternalProviderUnavailable);
        }
    }

    // -----------------------------------------------------------------------
    // OQ-5: reference byte-resolution from IGeneratedAssetStorage, capped to
    // ComfyUiSettings.MaxReferenceImages; failed resolves are logged and skipped so the
    // remaining references still proceed (CUI-01-a, CUI-01-b, CUI-06-a).
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<(byte[] Bytes, string Mime)>> ResolveReferenceImageBytesAsync(
        ImageGenerationRequest request,
        ComfyUiSettings settings,
        CancellationToken ct)
    {
        if (request.ReferenceImages is not { Count: > 0 })
        {
            return [];
        }

        var cap = Math.Min(request.ReferenceImages.Count, settings.MaxReferenceImages);
        var resolved = new List<(byte[], string)>(cap);

        for (var i = 0; i < cap; i++)
        {
            var refImage = request.ReferenceImages[i];
            if (string.IsNullOrWhiteSpace(refImage.Url))
            {
                continue;
            }

            Result<StoredAssetStream> openResult;
            try
            {
                openResult = await _storage.OpenAsync(refImage.Url, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "ComfyUiImageModelClient: failed to resolve reference image handle '{Url}' for org {OrganizationId}. Skipping this reference.",
                    refImage.Url,
                    request.OrganizationId);
                continue;
            }

            if (openResult.IsFailure)
            {
                _logger.LogWarning(
                    "ComfyUiImageModelClient: failed to open reference image handle '{Url}' for org {OrganizationId}: {Code}. Skipping this reference.",
                    refImage.Url,
                    request.OrganizationId,
                    openResult.Error.Code);
                continue;
            }

            try
            {
                using var stream = openResult.Value;
                using var ms = new MemoryStream();
                await stream.Content.CopyToAsync(ms, ct);
                resolved.Add((ms.ToArray(), stream.Mime));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "ComfyUiImageModelClient: failed to read reference image bytes from '{Url}' for org {OrganizationId}. Skipping this reference.",
                    refImage.Url,
                    request.OrganizationId);
            }
        }

        return resolved;
    }

    // -----------------------------------------------------------------------
    // OQ-2/OQ-5: upload -> queue -> poll -> fetch HTTP calls
    // -----------------------------------------------------------------------

    private static async Task<Result<string>> UploadReferenceImageAsync(
        HttpClient http,
        string baseUrl,
        (byte[] Bytes, string Mime) reference,
        CancellationToken ct)
    {
        using var multipart = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(reference.Bytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(reference.Mime) ? "image/png" : reference.Mime);
        multipart.Add(imageContent, "image", "reference.png");

        var response = await http.PostAsync($"{baseUrl}/upload/image", multipart, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var name = doc.RootElement.GetProperty("name").GetString();
        return Result.Success(name ?? string.Empty);
    }

    /// <summary>
    /// CUI-02-b: a non-2xx <c>/prompt</c> response or a queue rejection surfaced via
    /// <c>node_errors</c> both map to <see cref="IntegrationErrors.ExternalProviderUnavailable"/>.
    /// </summary>
    /// <summary>
    /// A5: the rejection outcome of a <c>POST /prompt</c> call. <see cref="RejectionPayload"/> is
    /// the raw response body on ANY failure path — non-2xx status, <c>node_errors</c>, or a missing
    /// <c>prompt_id</c> — so the caller can feed it to
    /// <see cref="ComfyUiDetailerProbe.IsDetailerAttributable"/> without re-parsing. <c>null</c> on
    /// success.
    /// </summary>
    private readonly record struct QueuePromptOutcome(Result<string> Result, string? RejectionPayload);

    private static async Task<QueuePromptOutcome> QueuePromptAsync(
        HttpClient http,
        string baseUrl,
        string graphJson,
        CancellationToken ct)
    {
        using var graphDoc = JsonDocument.Parse(graphJson);
        var body = JsonSerializer.Serialize(new
        {
            prompt = graphDoc.RootElement,
            client_id = Guid.NewGuid().ToString("N"),
        });

        var response = await http.PostAsync(
            $"{baseUrl}/prompt",
            new StringContent(body, Encoding.UTF8, "application/json"),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            return new QueuePromptOutcome(
                Result.Failure<string>(IntegrationErrors.ExternalProviderUnavailable),
                errorBody);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("node_errors", out var nodeErrors)
            && nodeErrors.ValueKind == JsonValueKind.Object
            && nodeErrors.EnumerateObject().Any())
        {
            return new QueuePromptOutcome(
                Result.Failure<string>(IntegrationErrors.ExternalProviderUnavailable),
                json);
        }

        var promptId = doc.RootElement.TryGetProperty("prompt_id", out var idProp) ? idProp.GetString() : null;
        return string.IsNullOrWhiteSpace(promptId)
            ? new QueuePromptOutcome(Result.Failure<string>(IntegrationErrors.ExternalProviderUnavailable), json)
            : new QueuePromptOutcome(Result.Success(promptId), null);
    }

    /// <summary>
    /// Polls <c>GET /history/{id}</c> every <see cref="ComfyUiSettings.PollInterval"/> until the
    /// configured <see cref="ComfyUiSettings.OutputNodeId"/> carries <c>images[]</c>. A
    /// <c>status.status_str == "error"</c> entry maps to
    /// <see cref="IntegrationErrors.ExternalProviderUnavailable"/> (CUI-02-b). The loop is bounded
    /// by the caller's <paramref name="ct"/> (already linked to the overall Timeout in
    /// <see cref="GenerateAsync"/>) — an elapsed/cancelled token propagates as
    /// <see cref="OperationCanceledException"/> and is mapped to
    /// <see cref="IntegrationErrors.ProviderTimeout"/> by the caller (CUI-02-c).
    /// </summary>
    /// <summary>
    /// Asks ComfyUI to reclaim video memory via <c>POST /free</c>.
    /// </summary>
    /// <param name="unloadModels">
    /// <c>false</c> drops cached tensors but keeps the checkpoint resident, so the next generation
    /// does not pay the model-load cost. <c>true</c> also evicts the model — reserved for recovering
    /// from an allocation failure, where keeping the checkpoint resident is what leaves no headroom.
    /// </param>
    /// <remarks>
    /// Best-effort: any failure is logged at debug and swallowed. This is a hygiene call, and a
    /// backend that does not implement <c>/free</c> (or is momentarily busy) must not turn a
    /// generation that would have worked into a failure.
    /// </remarks>
    private async Task TryReclaimVideoMemoryAsync(
        HttpClient http,
        string baseUrl,
        bool unloadModels,
        CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(
                $$"""{"unload_models":{{(unloadModels ? "true" : "false")}},"free_memory":true}""",
                Encoding.UTF8,
                "application/json");

            using var response = await http.PostAsync($"{baseUrl}/free", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "ComfyUI /free returned {StatusCode}; continuing without reclaiming video memory.",
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "ComfyUI /free call failed; continuing without reclaiming video memory.");
        }
    }

    /// <summary>
    /// Releases the prompt this call queued once the call has given up on it: drops it from the
    /// pending queue, and interrupts execution when it is the prompt currently running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordered delete-then-check on purpose. Deleting first removes a still-pending prompt so it can
    /// never start between the check and the action; a prompt that is already running is unaffected
    /// by the delete and is caught by the <c>GET /queue</c> read that follows.
    /// </para>
    /// <para>
    /// <c>POST /interrupt</c> carries no prompt id — it stops whatever the backend is executing — so
    /// it is issued ONLY after <c>GET /queue</c> confirms the running prompt is ours. A blanket
    /// interrupt would cancel an unrelated render on a shared backend.
    /// </para>
    /// <para>
    /// Runs on its own token: the caller's is already cancelled, which is precisely why we are here.
    /// Best-effort throughout — this is cleanup on a path that has already failed and must never
    /// change the error the caller sees.
    /// </para>
    /// </remarks>
    private async Task TryAbandonQueuedPromptAsync(string baseUrl, string? promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId))
        {
            // Nothing was queued, so there is nothing of ours to cancel.
            return;
        }

        try
        {
            using var cleanupCts = new CancellationTokenSource(CleanupTimeout);
            var cleanupCt = cleanupCts.Token;
            using var http = _httpClientFactory.CreateClient(nameof(ComfyUiImageModelClient));

            using var deleteContent = new StringContent(
                $$"""{"delete":[{{JsonSerializer.Serialize(promptId)}}]}""",
                Encoding.UTF8,
                "application/json");

            using var deleteResponse = await http.PostAsync($"{baseUrl}/queue", deleteContent, cleanupCt);
            if (!deleteResponse.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "ComfyUI queue delete for prompt {PromptId} returned {StatusCode}.",
                    promptId,
                    (int)deleteResponse.StatusCode);
            }

            if (!await IsPromptRunningAsync(http, baseUrl, promptId, cleanupCt))
            {
                return;
            }

            using var interruptContent = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            using var interruptResponse = await http.PostAsync($"{baseUrl}/interrupt", interruptContent, cleanupCt);

            _logger.LogInformation(
                "ComfyUI: interrupted abandoned prompt {PromptId} (status {StatusCode}).",
                promptId,
                (int)interruptResponse.StatusCode);
        }
        catch (Exception ex)
        {
            // Includes the cleanup budget elapsing. An unreachable or wedged backend cannot be told
            // to stop, and saying so louder would not help the caller.
            _logger.LogDebug(ex, "ComfyUI: could not abandon prompt {PromptId} on the backend.", promptId);
        }
    }

    /// <summary>
    /// Reads <c>GET /queue</c> and reports whether <paramref name="promptId"/> is the entry ComfyUI
    /// is currently executing. Each <c>queue_running</c> entry is a positional array whose second
    /// element is the prompt id. Returns <c>false</c> on any unexpected shape or failure: not
    /// knowing must never escalate into interrupting a render that may not be ours.
    /// </summary>
    private async Task<bool> IsPromptRunningAsync(
        HttpClient http,
        string baseUrl,
        string promptId,
        CancellationToken ct)
    {
        using var response = await http.GetAsync($"{baseUrl}/queue", ct);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var json = await response.Content.ReadAsStringAsync(ct);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("queue_running", out var running)
                || running.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var entry in running.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2)
                {
                    continue;
                }

                var idElement = entry[1];
                if (idElement.ValueKind == JsonValueKind.String
                    && string.Equals(idElement.GetString(), promptId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Extracts ComfyUI's own failure description from a history entry's <c>status</c> object. The
    /// detail lives in <c>messages</c> as an <c>["execution_error", {...}]</c> pair carrying the
    /// failing node and the backend exception (e.g. a KSampler out-of-memory). Returns a short
    /// single-line summary, or a fallback when the shape is not what we expect — this runs on a
    /// failure path and must never throw or mask the original error.
    /// </summary>
    internal static string DescribeExecutionError(JsonElement status)
    {
        try
        {
            if (status.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    if (message.ValueKind != JsonValueKind.Array
                        || message.GetArrayLength() < 2
                        || !string.Equals(message[0].GetString(), "execution_error", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var detail = message[1];
                    var nodeType = ReadString(detail, "node_type");
                    var exceptionType = ReadString(detail, "exception_type");
                    var exceptionMessage = ReadString(detail, "exception_message");

                    return $"node={nodeType} exception={exceptionType} message={exceptionMessage}";
                }
            }

            return "no execution_error detail in history status";
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            return "unreadable execution_error detail";
        }

        static string ReadString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "(null)"
                : "(unknown)";
    }

    /// <summary>
    /// A5 (execution-time counterpart to <see cref="QueuePromptOutcome"/>): <see cref="ExecutionErrorDetail"/>
    /// is <see cref="DescribeExecutionError"/>'s formatted text on an <c>/history</c> <c>status_str
    /// == "error"</c> outcome, so the caller can feed it to
    /// <see cref="ComfyUiDetailerProbe.IsDetailerAttributable"/> the same way it feeds a queue-time
    /// rejection payload. <c>null</c> on success or on a non-execution failure (unreachable
    /// <c>/history</c> endpoint).
    /// </summary>
    private readonly record struct PollHistoryOutcome(Result<JsonElement> Result, string? ExecutionErrorDetail);

    private static async Task<PollHistoryOutcome> PollHistoryAsync(
        HttpClient http,
        string baseUrl,
        string promptId,
        ComfyUiSettings settings,
        ILogger logger,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var response = await http.GetAsync($"{baseUrl}/history/{promptId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                return new PollHistoryOutcome(
                    Result.Failure<JsonElement>(IntegrationErrors.ExternalProviderUnavailable),
                    ExecutionErrorDetail: null);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty(promptId, out var entry))
            {
                if (entry.TryGetProperty("status", out var status)
                    && status.TryGetProperty("status_str", out var statusStr)
                    && string.Equals(statusStr.GetString(), "error", StringComparison.OrdinalIgnoreCase))
                {
                    // A workflow that FAILED TO EXECUTE is not the same thing as an unreachable
                    // provider, but both collapse into ExternalProviderUnavailable at the Result
                    // level. Log ComfyUI's own diagnosis first — without it, an out-of-memory
                    // KSampler is indistinguishable in the logs from a server that is simply down,
                    // which is exactly how a resolution regression once read as an outage.
                    var detail = DescribeExecutionError(status);
                    logger.LogError(
                        "ComfyUI workflow execution failed for prompt {PromptId}: {Detail}",
                        promptId,
                        detail);

                    return new PollHistoryOutcome(
                        Result.Failure<JsonElement>(IntegrationErrors.ExternalProviderUnavailable),
                        ExecutionErrorDetail: detail);
                }

                if (entry.TryGetProperty("outputs", out var outputs)
                    && outputs.TryGetProperty(settings.OutputNodeId, out var outputNode)
                    && outputNode.TryGetProperty("images", out var images)
                    && images.ValueKind == JsonValueKind.Array
                    && images.GetArrayLength() > 0)
                {
                    return new PollHistoryOutcome(Result.Success(images[0].Clone()), ExecutionErrorDetail: null);
                }
            }

            await Task.Delay(settings.PollInterval, ct);
        }
    }

    /// <summary>An empty <c>/view</c> body maps to <see cref="IntegrationErrors.AiJsonInvalid"/>.</summary>
    private static async Task<Result<byte[]>> FetchImageBytesAsync(
        HttpClient http,
        string baseUrl,
        JsonElement image,
        CancellationToken ct)
    {
        var filename = image.TryGetProperty("filename", out var f) ? f.GetString() : null;
        var subfolder = image.TryGetProperty("subfolder", out var sf) ? sf.GetString() : null;
        var type = image.TryGetProperty("type", out var t) ? t.GetString() : "output";

        var query = $"{baseUrl}/view?filename={Uri.EscapeDataString(filename ?? string.Empty)}" +
            $"&subfolder={Uri.EscapeDataString(subfolder ?? string.Empty)}" +
            $"&type={Uri.EscapeDataString(type ?? "output")}";

        var response = await http.GetAsync(query, ct);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<byte[]>(IntegrationErrors.ExternalProviderUnavailable);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return bytes.Length == 0
            ? Result.Failure<byte[]>(IntegrationErrors.AiJsonInvalid)
            : Result.Success(bytes);
    }

    // -----------------------------------------------------------------------
    // OQ-1: template loading + substitution + JSON validation
    // -----------------------------------------------------------------------

    /// <summary>
    /// A3/A6/A7 (CUI-08): resolves the workflow template used for this generation.
    /// <list type="bullet">
    /// <item>A per-org custom <see cref="ComfyUiSettings.WorkflowTemplateJson"/> override always wins
    /// — the probe never runs and never influences a user-supplied graph.</item>
    /// <item><see cref="ComfyUiSettings.DetailerEnabled"/> <c>== false</c> short-circuits straight to
    /// the plain bundled template WITHOUT even probing (task 5.3).</item>
    /// <item>Otherwise the probe decides: <see cref="ComfyUiDetailerSupport.Supported"/> selects the
    /// augmented template; every other outcome — <see cref="ComfyUiDetailerSupport.NotSupported"/>,
    /// <see cref="ComfyUiDetailerSupport.Unknown"/>, or any future unhandled branch — resolves to
    /// plain. This is the fallback invariant: non-`Supported` always means plain.</item>
    /// </list>
    /// </summary>
    private static async Task<(string TemplateJson, bool IsAugmented)> SelectTemplateAsync(
        HttpClient http,
        string baseUrl,
        ComfyUiSettings settings,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(settings.WorkflowTemplateJson))
        {
            return (settings.WorkflowTemplateJson, false);
        }

        if (!settings.DetailerEnabled)
        {
            return (LoadBundledDefaultTemplate(), false);
        }

        var support = await ComfyUiDetailerProbe.ProbeAsync(http, baseUrl, ct);

        return support == ComfyUiDetailerSupport.Supported
            ? (LoadBundledDetailerTemplate(), true)
            : (LoadBundledDefaultTemplate(), false);
    }

    private static string LoadBundledDefaultTemplate() => LoadEmbeddedResource(EmbeddedTemplateResourceName);

    private static string LoadBundledDetailerTemplate() => LoadEmbeddedResource(EmbeddedDetailerTemplateResourceName);

    private static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(ComfyUiImageModelClient).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in {assembly.FullName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Substitutes every <c>{{TOKEN}}</c> placeholder in <paramref name="template"/> with the
    /// JSON-escaped scalar values. When <paramref name="referenceImageName"/> is null/empty, the
    /// LoadImage/IP-Adapter chain is bypassed by rewriting the KSampler "model" edge to reference
    /// the checkpoint loader directly (descriptor-prompt-only path; design §3.3).
    /// </summary>
    internal static string SubstituteTemplate(
        string template,
        string checkpoint,
        string positivePrompt,
        string negativePrompt,
        string? referenceImageName,
        string? ipAdapterModel,
        int width,
        int height,
        long seed,
        int steps,
        double cfg)
    {
        var hasReference = !string.IsNullOrEmpty(referenceImageName);

        var substituted = template
            .Replace("{{CHECKPOINT_NAME}}", JsonEscape(checkpoint))
            .Replace("{{POSITIVE_PROMPT}}", JsonEscape(positivePrompt))
            .Replace("{{NEGATIVE_PROMPT}}", JsonEscape(negativePrompt))
            .Replace("{{REFERENCE_IMAGE}}", JsonEscape(referenceImageName ?? string.Empty))
            .Replace("{{IPADAPTER_MODEL}}", JsonEscape(ipAdapterModel ?? string.Empty))
            .Replace("{{WIDTH}}", width.ToString(CultureInfo.InvariantCulture))
            .Replace("{{HEIGHT}}", height.ToString(CultureInfo.InvariantCulture))
            .Replace("{{SEED}}", seed.ToString(CultureInfo.InvariantCulture))
            .Replace("{{STEPS}}", steps.ToString(CultureInfo.InvariantCulture))
            .Replace("{{CFG}}", cfg.ToString(CultureInfo.InvariantCulture));

        return hasReference
            ? substituted
            : substituted.Replace(ConditionedModelEdge, PassThroughModelEdge);
    }

    /// <summary>
    /// Substitutes the template and validates the result via <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/>.
    /// A parse failure maps to <see cref="IntegrationErrors.AiJsonInvalid"/>.
    /// </summary>
    internal static Result<string> BuildWorkflowGraph(
        string template,
        string checkpoint,
        string positivePrompt,
        string negativePrompt,
        string? referenceImageName,
        string? ipAdapterModel,
        int width,
        int height,
        long seed,
        int steps,
        double cfg)
    {
        var substituted = SubstituteTemplate(
            template,
            checkpoint,
            positivePrompt,
            negativePrompt,
            referenceImageName,
            ipAdapterModel,
            width,
            height,
            seed,
            steps,
            cfg);

        try
        {
            using var _ = JsonDocument.Parse(substituted);
        }
        catch (JsonException)
        {
            return Result.Failure<string>(IntegrationErrors.AiJsonInvalid);
        }

        return Result.Success(substituted);
    }

    private static string JsonEscape(string value)
    {
        var serialized = JsonSerializer.Serialize(value);
        return serialized[1..^1];
    }
}
