using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Abstractions.ContentAnalysis;

/// <summary>
/// Module-neutral port for selecting the default AI provider for a given organization.
/// Lives in the neutral Application core so all future module profiles (Social, E-Commerce, …)
/// inherit automatic AI provider selection. No Infrastructure or provider types leak through.
/// </summary>
public interface IDefaultAiSelector
{
    /// <summary>
    /// Returns the preferred AI provider and model for <paramref name="organizationId"/>
    /// by applying the default-AI selection rule:
    /// 1. First: integration marked <c>is_default_ai = true</c> (connected, enabled).
    /// 2. Fallback: most recently configured AiModel integration (OrderByDescending UpdatedAtUtc).
    /// Returns <see cref="Domain.Integrations.IntegrationErrors.DefaultAiProviderNotConnected"/>
    /// when no AiModel integration exists.
    /// </summary>
    Task<Result<DefaultAiSelection>> SelectAsync(
        Guid organizationId,
        CancellationToken ct = default);
}

/// <summary>
/// The selected AI provider key and optional model name, resolved by <see cref="IDefaultAiSelector"/>.
/// </summary>
public sealed record DefaultAiSelection(string ProviderKey, string? Model);
