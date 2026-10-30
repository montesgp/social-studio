using System.Security.Cryptography;
using System.Text;

namespace SocialStudio.Domain.Social.Workflow;

/// <summary>
/// Deterministic idempotency key for one candidate's enqueue attempt. Stable regardless of the
/// caller's format-list order; differs whenever the workflow, candidate, or composition content
/// changes. Used to enforce "double-click does not double-bill" via a partial unique index.
/// </summary>
public static class WorkflowCandidateEnqueueKey
{
    public static string Create(
        Guid workflowId,
        Guid candidateId,
        IReadOnlyList<(string FormatKey, int Count)> composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        var normalized = composition
            .OrderBy(entry => entry.FormatKey, StringComparer.Ordinal)
            .Select(entry => $"{entry.FormatKey}:{entry.Count}");

        var payload = string.Join('|', normalized);
        var input = $"{workflowId:N}:{candidateId:N}:{payload}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }
}
