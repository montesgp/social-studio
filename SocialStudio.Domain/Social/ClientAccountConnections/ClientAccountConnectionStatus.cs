namespace SocialStudio.Domain.Social.ClientAccountConnections;

/// <summary>Mirrors the <c>OrganizationIntegration</c> connection-test lifecycle so a "Test connection"
/// action is uniform across the org-level and per-client credential levels.</summary>
public enum ClientAccountConnectionStatus
{
    Connected = 0,
    Failed = 1,
    Disabled = 2,
}
