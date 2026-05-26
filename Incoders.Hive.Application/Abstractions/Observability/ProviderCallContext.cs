namespace Incoders.Hive.Application.Abstractions.Observability;

/// <summary>
/// Ambient context (scoped DI service) that the orchestrator populates before each
/// provider call. Infrastructure decorators read it to populate <see cref="UsageEvent"/>
/// fields without coupling to business-layer types.
/// </summary>
public sealed class ProviderCallContext
{
    private ProviderCallEntry? _current;

    /// <summary>Gets the current ambient context, or <c>null</c> if not set.</summary>
    public ProviderCallEntry? Current => _current;

    /// <summary>
    /// Begins a provider call scope. Returns an <see cref="IDisposable"/> that resets
    /// the context when disposed — suitable for a <c>using</c> block.
    /// </summary>
    public IDisposable Begin(
        Guid organizationId,
        Guid? clientId,
        string workflowKey,
        string pipelineStepKey,
        string? promptVersion,
        Guid correlationId)
    {
        _current = new ProviderCallEntry(
            organizationId,
            clientId,
            workflowKey,
            pipelineStepKey,
            promptVersion,
            correlationId);

        return new ResetScope(this);
    }

    /// <summary>Clears the current scope.</summary>
    public void Reset() => _current = null;

    private sealed class ResetScope : IDisposable
    {
        private readonly ProviderCallContext _ctx;
        private bool _disposed;

        public ResetScope(ProviderCallContext ctx) => _ctx = ctx;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ctx.Reset();
        }
    }
}

/// <summary>Immutable snapshot of a provider call scope entry.</summary>
public sealed record ProviderCallEntry(
    Guid OrganizationId,
    Guid? ClientId,
    string WorkflowKey,
    string PipelineStepKey,
    string? PromptVersion,
    Guid CorrelationId);
