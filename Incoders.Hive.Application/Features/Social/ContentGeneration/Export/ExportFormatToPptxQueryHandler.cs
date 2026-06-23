using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Export;

/// <summary>
/// Handles <see cref="ExportFormatToPptxQuery"/>.
///
/// <para>Algorithm:</para>
/// <list type="number">
///   <item><description>Tenant-scope: verify the job belongs to <c>OrganizationId</c>.</description></item>
///   <item><description>Load format results for the job; find the row matching <c>FormatKey</c>.</description></item>
///   <item><description>If <c>CompositionSpecJson</c> is absent → return <c>not-exportable</c> (no crash, no re-design).</description></item>
///   <item><description>Deserialize the envelope; delegate to <see cref="IPptxExportAdapter.BuildAsync"/>.</description></item>
///   <item><description>Return the PPTX bytes and a filename derived from the format key.</description></item>
/// </list>
///
/// <para>Spec: PPTX-02 (editable download), PPTX-04 (single markup retrieval path).</para>
/// </summary>
public sealed class ExportFormatToPptxQueryHandler
    : IRequestHandler<ExportFormatToPptxQuery, Result<ExportFormatToPptxResponse>>
{
    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IContentGenerationFormatResultRepository _formatResultRepository;
    private readonly IPptxExportAdapter _pptxAdapter;

    public ExportFormatToPptxQueryHandler(
        IContentGenerationJobRepository jobRepository,
        IContentGenerationFormatResultRepository formatResultRepository,
        IPptxExportAdapter pptxAdapter)
    {
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(formatResultRepository);
        ArgumentNullException.ThrowIfNull(pptxAdapter);

        _jobRepository = jobRepository;
        _formatResultRepository = formatResultRepository;
        _pptxAdapter = pptxAdapter;
    }

    public async Task<Result<ExportFormatToPptxResponse>> Handle(
        ExportFormatToPptxQuery query,
        CancellationToken cancellationToken)
    {
        // 1. Tenant-scoped job lookup
        var job = await _jobRepository.GetByIdAsync(
            query.JobId,
            query.OrganizationId,
            cancellationToken);

        if (job is null)
        {
            return Result.Failure<ExportFormatToPptxResponse>(
                ContentGenerationErrors.JobNotFound);
        }

        // 2. Load all format results for this job and find the requested format
        var formatResults = await _formatResultRepository.ListByJobAsync(
            query.JobId,
            cancellationToken);

        var formatResult = formatResults.FirstOrDefault(r =>
            string.Equals(r.FormatKey, query.FormatKey, StringComparison.OrdinalIgnoreCase));

        if (formatResult is null)
        {
            return Result.Failure<ExportFormatToPptxResponse>(
                Error.NotFound(
                    "content-gen.format-not-found",
                    $"No stored result found for format '{query.FormatKey}'."));
        }

        // 3. Check that markup was persisted (predates B3 or render failed)
        //    Spec PPTX-02-d: return a clear user-facing error, NOT an exception.
        if (string.IsNullOrWhiteSpace(formatResult.CompositionSpecJson))
        {
            return Result.Failure<ExportFormatToPptxResponse>(
                ContentGenerationErrors.NotExportable);
        }

        // 4. Parse the persisted envelope(s): one per slide for carousel, one for single formats.
        //    Accepts both the legacy single-object shape and the multi-slide array shape.
        var envelopes = CompositionSpecJson.ParseActiveList(formatResult.CompositionSpecJson);
        if (envelopes.Count == 0)
        {
            return Result.Failure<ExportFormatToPptxResponse>(
                ContentGenerationErrors.NotExportable);
        }

        // 5. Delegate to the PPTX adapter (offline, deterministic) — one slide per envelope.
        var buildResult = await _pptxAdapter.BuildManyAsync(envelopes, cancellationToken);

        if (buildResult.IsFailure)
        {
            return Result.Failure<ExportFormatToPptxResponse>(buildResult.Error);
        }

        var filename = $"{query.FormatKey}.pptx";

        return Result.Success(new ExportFormatToPptxResponse(
            PptxBytes: buildResult.Value,
            DownloadFilename: filename));
    }
}
