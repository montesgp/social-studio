using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Orchestration;

/// <summary>
/// Registry that maps format keys to their corresponding generators.
/// Populated at startup via DI (IEnumerable&lt;IFormatContentGenerator&gt;).
/// </summary>
public sealed class FormatContentGeneratorRegistry
{
    private readonly IReadOnlyDictionary<string, IFormatContentGenerator> _generators;

    public FormatContentGeneratorRegistry(IEnumerable<IFormatContentGenerator> generators)
    {
        ArgumentNullException.ThrowIfNull(generators);
        _generators = generators.ToDictionary(g => g.FormatKey, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the generator for the given format key, or null if not registered.
    /// </summary>
    public IFormatContentGenerator? TryResolve(string formatKey) =>
        _generators.TryGetValue(formatKey, out var gen) ? gen : null;

    /// <summary>All registered format keys.</summary>
    public IReadOnlyCollection<string> RegisteredKeys => _generators.Keys.ToArray();
}
