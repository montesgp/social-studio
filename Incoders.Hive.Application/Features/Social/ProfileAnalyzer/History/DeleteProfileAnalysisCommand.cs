using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.History;

/// <summary>Hard-deletes a profile analysis, org-scoped (PA-15).</summary>
public sealed record DeleteProfileAnalysisCommand(
    Guid AnalysisId,
    Guid OrganizationId) : IRequest<Result>;

public sealed class DeleteProfileAnalysisCommandHandler
    : IRequestHandler<DeleteProfileAnalysisCommand, Result>
{
    private readonly IProfileAnalysisRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteProfileAnalysisCommandHandler(
        IProfileAnalysisRepository repository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        DeleteProfileAnalysisCommand request,
        CancellationToken cancellationToken)
    {
        var deleted = await _repository.DeleteAsync(
            request.AnalysisId,
            request.OrganizationId,
            cancellationToken);

        if (!deleted)
            return Result.Failure(ProfileAnalysisErrors.NotFound);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
