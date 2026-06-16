using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Export;

/// <summary>
/// Single composition path for package-level editable exports. Both "Send to Canva" and
/// assembled package downloads use this helper so the editable PPTX artifact is produced
/// from the same persisted composition specs, in the same page order.
/// </summary>
internal static class PackagePptxComposer
{
    private static readonly string[] VisualFormatOrder = ["story", "feed-post", "carousel"];

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
                envelopes.AddRange(CompositionSpecJson.ParseList(result.CompositionSpecJson));
            }
        }

        return envelopes;
    }
}
