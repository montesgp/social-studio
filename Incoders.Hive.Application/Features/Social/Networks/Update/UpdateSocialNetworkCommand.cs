using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Networks.Update;

// Code NOT included in this command — ADR 6: Code is immutable post-create
public sealed record UpdateSocialNetworkCommand(
    Guid Id,
    string DisplayName,
    string IconKey) : IRequest<Result<SocialNetworkResponse>>;
