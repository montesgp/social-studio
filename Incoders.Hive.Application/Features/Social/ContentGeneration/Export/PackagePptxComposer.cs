using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentFormats;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Export;

/// <summary>
/// Single composition path for package-level editable exports. Both "Send to Canva" and
/// assembled package downloads use this helper so the editable PPTX artifact is produced
/// from the same persisted composition specs, in the same page order.
/// </summary>
internal static class PackagePptxComposer
{
    /// <summary>
    /// Ordered list of visual format keys for PPTX export.
    /// Derived from <see cref="FormatCatalog"/> in catalog declaration order. Legacy single keys
    /// ("story", "feed-post") and legacy "carousel" are appended so historical packages remain
    /// exportable through the same editable PPTX path.
    /// </summary>
    private static readonly IReadOnlyList<string> VisualFormatOrder =
        FormatCatalog.All()
            .Where(d => d.IsVisual)
            .Select(d => d.FormatKey)
            .Append("story")
            .Append("feed-post")
            .Append("carousel") // legacy alias for historical export ordering
            .ToList();

    public static async Task<Result<byte[]>> BuildAsync(
        IReadOnlyList<ContentGenerationFormatResult> formatResults,
        IPptxExportAdapter pptxAdapter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formatResults);
        ArgumentNullException.ThrowIfNull(pptxAdapter);

        var envelopes = GetEditableEnvelopes(formatResults);
        if (envelopes.Count == 0)
        {
            return Result.Failure<byte[]>(ContentGenerationErrors.NotExportable);
        }

        return await pptxAdapter.BuildManyAsync(envelopes, cancellationToken);
    }

    private static IReadOnlyList<CompositionSpecEnvelope> GetEditableEnvelopes(
        IReadOnlyList<ContentGenerationFormatResult> formatResults)
    {
        var byFormat = formatResults
            .GroupBy(r => r.FormatKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.UpdatedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        var envelopes = new List<CompositionSpecEnvelope>();
        foreach (var formatKey in VisualFormatOrder)
        {
            if (byFormat.TryGetValue(formatKey, out var result))
            {
                envelopes.AddRange(CompositionSpecJson.ParseActiveList(result.CompositionSpecJson));
            }
        }

        return envelopes;
    }
}
