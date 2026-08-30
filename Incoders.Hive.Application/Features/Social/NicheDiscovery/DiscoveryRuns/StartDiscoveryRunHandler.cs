using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

public sealed class StartDiscoveryRunHandler : IRequestHandler<StartDiscoveryRunCommand, Result<StartDiscoveryRunResponse>>
{
    // AUTHORED, not literal from design: design names the error code (content_format_invalid) but
    // never enumerates the allowed formats. YouTube's two content shapes are the only ones this
    // change's origin (WU-5) can source evidence for.
    private static readonly string[] AllowedContentFormats = ["video", "short"];

    // Mirrors design §5.2's workflow-step SupportedNetworkKeys for this change's single network.
    private static readonly string[] SupportedNetworkKeys = ["youtube"];

    private readonly IDiscoveryRunRepository _runRepository;
    private readonly INicheDiscoveryRunQueue _queue;
    private readonly IMarketCatalogRepository _marketCatalog;
    private readonly IClock _clock;

    public StartDiscoveryRunHandler(
        IDiscoveryRunRepository runRepository,
        INicheDiscoveryRunQueue queue,
        IMarketCatalogRepository marketCatalog,
        IClock clock)
    {
        _runRepository = runRepository;
        _queue = queue;
        _marketCatalog = marketCatalog;
        _clock = clock;
    }

    public async Task<Result<StartDiscoveryRunResponse>> Handle(StartDiscoveryRunCommand request, CancellationToken cancellationToken)
    {
        if (!SupportedNetworkKeys.Contains(request.NetworkKey, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<StartDiscoveryRunResponse>(NicheDiscoveryErrors.NetworkUnsupported);
        }

        if (!AllowedContentFormats.Contains(request.ContentFormat, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<StartDiscoveryRunResponse>(NicheDiscoveryErrors.ContentFormatInvalid);
        }

        var sourceMarketValidation = await ValidateMarketAsync(request.SourceMarketCode, cancellationToken);
        if (sourceMarketValidation.IsFailure)
        {
            return Result.Failure<StartDiscoveryRunResponse>(sourceMarketValidation.Error);
        }

        var targetMarketValidation = await ValidateMarketAsync(request.TargetMarketCode, cancellationToken);
        if (targetMarketValidation.IsFailure)
        {
            return Result.Failure<StartDiscoveryRunResponse>(targetMarketValidation.Error);
        }

        var window = request.WindowDays.HasValue
            ? DiscoveryWindow.Create(request.WindowDays.Value)
            : Result.Success(DiscoveryWindow.Default());
        if (window.IsFailure)
        {
            return Result.Failure<StartDiscoveryRunResponse>(window.Error);
        }

        DiscoveryDepth depth;
        if (string.IsNullOrWhiteSpace(request.Depth))
        {
            depth = DiscoveryDepth.Standard;
        }
        else if (!Enum.TryParse(request.Depth, ignoreCase: true, out depth) || !Enum.IsDefined(typeof(DiscoveryDepth), depth))
        {
            return Result.Failure<StartDiscoveryRunResponse>(NicheDiscoveryErrors.DepthUnknown);
        }

        var nowUtc = _clock.UtcNow;
        var snapshotJson = new DiscoveryRunConfigurationSnapshot(request.SeedTerms, request.Weights).ToJson();

        var runResult = DiscoveryRun.Create(
            request.OrganizationId,
            request.UserId,
            request.SourceMarketCode,
            request.TargetMarketCode,
            request.NetworkKey,
            request.ContentFormat,
            window.Value,
            depth,
            snapshotJson,
            request.NicheId,
            nowUtc);

        if (runResult.IsFailure)
        {
            return Result.Failure<StartDiscoveryRunResponse>(runResult.Error);
        }

        var run = runResult.Value;
        await _runRepository.AddAsync(run, cancellationToken);
        await _queue.EnqueueAsync(request.OrganizationId, run.Id, cancellationToken);

        return Result.Success(new StartDiscoveryRunResponse(run.Id.Value, run.Status.ToString()));
    }

    private async Task<Result> ValidateMarketAsync(string rawCode, CancellationToken ct)
    {
        var parsed = MarketCode.TryParse(rawCode);
        if (parsed.IsFailure)
        {
            return Result.Failure(NicheDiscoveryErrors.MarketCodeInvalid);
        }

        var entry = await _marketCatalog.GetByCodeAsync(parsed.Value, ct);
        if (entry is null)
        {
            return Result.Failure(NicheDiscoveryErrors.MarketUnknown);
        }

        if (!entry.IsActive)
        {
            return Result.Failure(NicheDiscoveryErrors.MarketInactive);
        }

        return Result.Success();
    }
}
