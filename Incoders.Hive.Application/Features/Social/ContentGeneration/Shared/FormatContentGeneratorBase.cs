using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Base class for all format content generators.
/// Handles the common pattern:
///   1. Begin ProviderCallContext
///   2. Call IAiCompletionGateway.CompleteJsonAsync
///   3. Map result to FormatGenerationResult
///   4. Track InferredFields via InferenceTracker
/// </summary>
public abstract class FormatContentGeneratorBase<TPayload> : IFormatContentGenerator<TPayload>
    where TPayload : notnull
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected readonly IAiCompletionGateway AiGateway;
    protected readonly ProviderCallContext ProviderCallContext;
    protected readonly ILogger Logger;

    protected FormatContentGeneratorBase(
        IAiCompletionGateway aiGateway,
        ProviderCallContext providerCallContext,
        ILogger? logger = null)
    {
        AiGateway = aiGateway ?? throw new ArgumentNullException(nameof(aiGateway));
        ProviderCallContext = providerCallContext ?? throw new ArgumentNullException(nameof(providerCallContext));
        Logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public abstract string FormatKey { get; }

    /// <inheritdoc />
    public abstract string PromptVersion { get; }

    /// <summary>
    /// Build the prompt input JSON for the AI call. Override in each generator.
    /// </summary>
    protected abstract string BuildPromptJson(GenerationContext ctx);

    /// <summary>
    /// Maps inferred fields from the brand snapshot into the result's InferredFields list.
    /// Override to declare which output fields are derived from inferred brand data.
    /// </summary>
    protected virtual IReadOnlyList<string> ResolveInferredFields(
        GenerationContext ctx,
        TPayload payload)
    {
        var tracker = new InferenceTracker(ctx.BrandSnapshot.InferredFields);
        PopulateInferredFields(tracker, ctx, payload);
        return tracker.GetInferredOutputFields();
    }

    /// <summary>
    /// Override to add format-specific inferred field tracking.
    /// Default: tracks standard brand fields mapped to output dot-notation paths.
    /// </summary>
    protected virtual void PopulateInferredFields(
        InferenceTracker tracker,
        GenerationContext ctx,
        TPayload payload)
    {
        tracker.TrackIfInferred("ColorPalette", "brand.ColorPalette");
        tracker.TrackIfInferred("VoiceAndTone", "brand.VoiceAndTone");
        tracker.TrackIfInferred("Logos", "brand.Logos");
        tracker.TrackIfInferred("BackgroundTemplates", "brand.BackgroundTemplates");
        tracker.TrackIfInferred("Typography", "brand.Typography");
        tracker.TrackIfInferred("ContentPillars", "brand.ContentPillars");
        tracker.TrackIfInferred("ToneKeywords", "brand.ToneKeywords");
        tracker.TrackIfInferred("TargetAudience", "brand.TargetAudience");
        tracker.TrackIfInferred("PostingFrequency", "brand.PostingFrequency");
        tracker.TrackIfInferred("MonthlyObjectives", "brand.MonthlyObjectives");
        tracker.TrackIfInferred("MonthlyKpis", "brand.MonthlyKpis");
        tracker.TrackIfInferred("ClientName", "brand.ClientName");
        tracker.TrackIfInferred("ClientDescription", "brand.ClientDescription");
        tracker.TrackIfInferred("ClientSectorOrNiche", "brand.ClientSectorOrNiche");
        tracker.TrackIfInferred("ReferentAccounts", "brand.ReferentAccounts");
        tracker.TrackIfInferred("CompetitorAccounts", "brand.CompetitorAccounts");
    }

    /// <inheritdoc />
    public async Task<FormatGenerationResult<TPayload>> GenerateAsync(
        GenerationContext ctx,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var promptJson = BuildPromptJson(ctx);

        Result<AiJsonCompletionResponse<TPayload>> aiResult;
        using (ProviderCallContext.Begin(
            ctx.OrganizationId,
            ctx.ClientId,
            workflowKey: "content-generation",
            pipelineStepKey: $"format:{FormatKey}",
            promptVersion: PromptVersion,
            correlationId: ctx.CorrelationId))
        {
            aiResult = await AiGateway.CompleteJsonAsync<TPayload>(
                new AiJsonCompletionRequest(
                    ctx.OrganizationId,
                    ctx.AnalysisId,
                    "content-generation",
                    PromptVersion,
                    promptJson,
                    ctx.AiProviderKey,
                    ctx.AiModel),
                cancellationToken);
        }

        if (aiResult.IsFailure)
        {
            Logger.LogWarning(
                "Content generation format {FormatKey} failed for analysis {AnalysisId}: {ErrorCode}",
                FormatKey, ctx.AnalysisId, aiResult.Error.Code);

            return FormatGenerationResult<TPayload>.Failed(PromptVersion, "provider-unavailable");
        }

        var payload = aiResult.Value.Payload;
        var inferred = ResolveInferredFields(ctx, payload);

        return FormatGenerationResult<TPayload>.Succeeded(payload, PromptVersion, inferred);
    }

    /// <inheritdoc />
    public async Task<FormatGenerationResult<object>> GenerateBoxedAsync(
        GenerationContext ctx,
        CancellationToken cancellationToken)
    {
        var typed = await GenerateAsync(ctx, cancellationToken);
        return typed.AsObject();
    }
}
