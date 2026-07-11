using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Shared;

/// <summary>
/// Issue #58 (LEAN fix): resolves storage-path background image references embedded in a content
/// item's persisted visual-spec markup (<c>composition_spec_json</c>) into browser-fetchable
/// data-URIs, ON READ ONLY, so the studio/package preview renders the AI-generated slide
/// background again.
///
/// <para>
/// Root cause (#56): the persisted markup's background layer references a Supabase storage path
/// (e.g. <c>.../feed-carousel/slide_1_bg.png</c>) that is not directly fetchable by the browser.
/// This resolver post-processes the markup returned to the client, replacing each storage-path
/// background reference with a data-URI read from storage — reusing the exact path-to-bytes
/// resolution already used by <c>PptxExportAdapter</c> and
/// <c>ContentGenerationJobProcessor.InlineBackgroundUrlAsync</c> for the same
/// <see cref="Pipeline.CompositionSpecEnvelope.BackgroundImageRef"/> field.
/// </para>
///
/// <para>
/// Resolution rules:
/// <list type="bullet">
///   <item><description>A legacy <c>data:</c> URI is left as-is (already fetchable, back-compat).</description></item>
///   <item><description>An <c>http(s):</c> URL is left as-is.</description></item>
///   <item><description>A storage path is opened via <see cref="IGeneratedAssetStorage"/> and inlined
///   as a <c>data:</c> URI in both the envelope's <c>BackgroundImageRef</c> field and every
///   occurrence of the same literal reference inside <c>Markup</c> (e.g. the enforced-background
///   <c>&lt;img src="..."&gt;</c>).</description></item>
///   <item><description>A missing/unresolvable file leaves the envelope unchanged — never throws
///   (BGREF-04 semantics).</description></item>
/// </list>
/// </para>
///
/// <para>
/// This resolver is READ-ONLY: it never persists the inlined result back to the DB. Persisting a
/// multi-MB data-URI in <c>composition_spec_json</c> would re-introduce the exact payload bloat
/// that #56/BGREF-01 fixed.
/// </para>
/// </summary>
public static class StudioVisualMarkupResolver
{
    public static async Task<string?> ResolveBackgroundRefsAsync(
        string? visualSpecMarkup,
        IGeneratedAssetStorage? storage,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(visualSpecMarkup) || storage is null)
        {
            return visualSpecMarkup;
        }

        var envelopes = CompositionSpecJson.ParseList(visualSpecMarkup);
        if (envelopes.Count == 0)
        {
            return visualSpecMarkup;
        }

        var resolved = new List<CompositionSpecEnvelope>(envelopes.Count);
        var changed = false;

        foreach (var envelope in envelopes)
        {
            var reference = envelope.BackgroundImageRef;
            if (string.IsNullOrWhiteSpace(reference) || IsAlreadyFetchable(reference))
            {
                resolved.Add(envelope);
                continue;
            }

            // Reuses the exact storage-path -> data-URI resolution already used for this same
            // BackgroundImageRef field by the PPTX export path (best-effort; original value is
            // returned unchanged on any failure — never throws).
            var dataUri = await ContentGenerationJobProcessor.InlineBackgroundUrlAsync(storage, reference, ct);
            if (string.Equals(dataUri, reference, StringComparison.Ordinal))
            {
                // Resolution failed or was a no-op — leave this envelope unchanged (BGREF-04).
                resolved.Add(envelope);
                continue;
            }

            changed = true;
            var updatedMarkup = envelope.Markup.Replace(reference, dataUri, StringComparison.Ordinal);
            resolved.Add(envelope with { Markup = updatedMarkup, BackgroundImageRef = dataUri });
        }

        return changed ? CompositionSpecJson.Serialize(resolved) : visualSpecMarkup;
    }

    private static bool IsAlreadyFetchable(string reference) =>
        reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
