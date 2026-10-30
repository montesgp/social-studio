using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Niches;
using MediatR;

namespace SocialStudio.Application.Features.Social.Niches.Create;

public sealed class CreateNicheHandler
    : IRequestHandler<CreateNicheCommand, Result<NicheResponse>>
{
    private readonly INicheRepository _niches;
    private readonly IClock _clock;

    public CreateNicheHandler(INicheRepository niches, IClock clock)
    {
        _niches = niches;
        _clock = clock;
    }

    public async Task<Result<NicheResponse>> Handle(
        CreateNicheCommand request,
        CancellationToken cancellationToken)
    {
        var code = (request.Code ?? string.Empty).Trim();
        var displayName = (request.DisplayName ?? string.Empty).Trim();

        // Pre-checks are the primary path; the DB unique indexes are defense-in-depth.
        var byCode = await _niches.GetByCodeAsync(code, cancellationToken);
        if (byCode is not null)
        {
            return Result.Failure<NicheResponse>(NicheErrors.CodeAlreadyExists(code));
        }

        var byDisplayName = await _niches.GetByDisplayNameAsync(displayName, cancellationToken);
        if (byDisplayName is not null)
        {
            return Result.Failure<NicheResponse>(NicheErrors.DisplayNameAlreadyExists(displayName));
        }

        var created = Niche.Create(
            code,
            displayName,
            request.IconKey,
            request.SearchHashtags ?? [],
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<NicheResponse>(created.Error);
        }

        await _niches.AddAsync(created.Value, cancellationToken);
        await _niches.SaveChangesAsync(cancellationToken);

        return Result.Success(NicheResponse.FromDomain(created.Value));
    }
}
