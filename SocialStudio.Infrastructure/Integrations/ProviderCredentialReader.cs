using SocialStudio.Application.Abstractions.Integrations;

namespace SocialStudio.Infrastructure.Integrations;

internal static class ProviderCredentialReader
{
    public static bool TryGet(
        ResolvedProviderCredential credential,
        string key,
        out string value)
    {
        if (credential.Credentials.TryGetValue(key, out value!)
            && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var camelKey = char.ToLowerInvariant(key[0]) + key[1..];
        return credential.Credentials.TryGetValue(camelKey, out value!)
            && !string.IsNullOrWhiteSpace(value);
    }
}
