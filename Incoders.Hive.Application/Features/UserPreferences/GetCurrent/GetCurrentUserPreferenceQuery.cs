using Incoders.Hive.Domain.Common;
using MediatR;
using Incoders.Hive.Application.Features.UserPreferences;

namespace Incoders.Hive.Application.Features.UserPreferences.GetCurrent;

public sealed record GetCurrentUserPreferenceQuery : IRequest<Result<UserPreferenceResponse>>;
