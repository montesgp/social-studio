namespace Incoders.Hive.Infrastructure.Identity;

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc);
