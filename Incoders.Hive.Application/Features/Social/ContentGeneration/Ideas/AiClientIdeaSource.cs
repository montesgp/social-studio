using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Ideas;

/// <summary>What the model must return: nothing but topics.</summary>
public sealed record GeneratedIdeaList(IReadOnlyList<GeneratedIdea>? Ideas);

public sealed record GeneratedIdea(string? Title, string? Summary);

/// <summary>
/// An idea source backed by a model, grounded by an <see cref="IClientIdeaGrounding"/>.
///
/// ONE class serves both "ideas from the client's strategy" and "random ideas from everything we know
/// about the client": the difference lives entirely in the grounding, and a third grounding is a new
/// registration rather than a new generator.
///
/// Ideas from here carry NO signal — a model-invented topic has no view count, and inventing one
/// would let it outrank a genuinely winning post scraped from the market.
/// </summary>
public sealed class AiClientIdeaSource : IContentIdeaSource
{
    private readonly IClientIdeaGrounding _grounding;
    private readonly IAiCompletionGateway _ai;
    private readonly string _capabilityKey;
    private readonly string _promptVersionKey;

    public AiClientIdeaSource(
        string sourceKey,
        int order,
        IClientIdeaGrounding grounding,
        IAiCompletionGateway ai,
        string capabilityKey,
        string promptVersionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentNullException.ThrowIfNull(grounding);
        ArgumentNullException.ThrowIfNull(ai);

        SourceKey = sourceKey;
        Order = order;
        _grounding = grounding;
        _ai = ai;
        _capabilityKey = capabilityKey;
        _promptVersionKey = promptVersionKey;
    }

    public string SourceKey { get; }

    public int Order { get; }

    /// <summary>
    /// Invented, whatever the grounding. The grounding shapes the topics; it never makes them somebody
    /// else's published content.
    /// </summary>
    public IdeaOrigin Origin => IdeaOrigin.Invented;

    public async Task<IReadOnlyList<ContentIdea>> DrawAsync(ContentIdeaRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Count <= 0)
        {
            return [];
        }

        var grounding = await _grounding.BuildAsync(request.OrganizationId, request.ClientId, ct);

        // Nothing to stand on — the defining case being a client with no strategy authored yet.
        // Empty, not an error: the chain simply moves to the next source.
        if (string.IsNullOrWhiteSpace(grounding))
        {
            return [];
        }

        // Built as a map rather than an anonymous type so an absent seed is genuinely ABSENT. Serializing
        // "variationSeed": null would change the payload of the ordinary grounded batch path, whose
        // output is meant to stay reproducible for the same client.
        var payload = new Dictionary<string, object>(3)
        {
            ["count"] = request.Count,
            ["grounding"] = grounding,
        };

        if (!string.IsNullOrWhiteSpace(request.VariationSeed))
        {
            // The model is asked to riff differently, not to answer a different question: same grounding,
            // same count, one varying token. Without it the provider is free to return — and cache — the
            // identical topics on every run, which is the opposite of "spontaneous".
            payload["variationSeed"] = request.VariationSeed;
        }

        var input = JsonSerializer.Serialize(payload);

        var completion = await _ai.CompleteJsonAsync<GeneratedIdeaList>(
            new AiJsonCompletionRequest(
                OrganizationId: request.OrganizationId,
                AnalysisId: null,
                CapabilityKey: _capabilityKey,
                PromptVersionKey: _promptVersionKey,
                InputJson: input,
                PreferredProviderKey: null,
                PreferredModel: null),
            ct);

        if (completion.IsFailure)
        {
            // A provider outage is a FAILURE, not "this source has nothing to say". Throwing lets the
            // chain record it in FailedSourceKeys so the operator sees why the batch came up short,
            // instead of silently receiving fewer pieces.
            throw new InvalidOperationException(
                $"Idea source '{SourceKey}' failed: {completion.Error.Code}");
        }

        var ideas = completion.Value.Payload.Ideas ?? [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var drawn = new List<ContentIdea>(Math.Min(ideas.Count, request.Count));

        foreach (var idea in ideas)
        {
            if (drawn.Count >= request.Count)
            {
                break;
            }

            var title = idea.Title?.Trim();

            // A blank topic would become a piece with no brief. Drop it rather than generate noise.
            if (string.IsNullOrWhiteSpace(title) || !seen.Add(title))
            {
                continue;
            }

            drawn.Add(new ContentIdea(
                Title: title,
                Summary: string.IsNullOrWhiteSpace(idea.Summary) ? null : idea.Summary.Trim(),
                SourceKey: SourceKey,
                Reference: null,
                Signal: null));
        }

        return drawn;
    }
}
