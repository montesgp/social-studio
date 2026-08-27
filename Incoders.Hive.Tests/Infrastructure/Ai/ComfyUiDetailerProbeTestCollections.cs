namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// xUnit collection that runs every test class touching <c>ComfyUiDetailerProbe</c>'s static,
/// process-wide cache serially (not in parallel with any other test collection).
/// <c>ComfyUiDetailerProbeTests</c> calls <c>ResetCacheForTests()</c> per test, which clears the
/// ENTIRE dictionary — not just its own key — so running it concurrently with
/// <c>ComfyUiDetailerSelectionTests</c> or <c>ComfyUiDetailerInvalidationTests</c> (different test
/// classes, which xUnit parallelizes by default) intermittently wipes a still-fresh cache entry
/// those tests rely on, producing a spurious extra <c>/object_info</c> probe. Isolating the
/// collection removes the cross-class race without changing what any test asserts.
/// </summary>
[CollectionDefinition("ComfyUiDetailerProbeCacheSerial", DisableParallelization = true)]
public sealed class ComfyUiDetailerProbeCacheSerialCollection
{
}
