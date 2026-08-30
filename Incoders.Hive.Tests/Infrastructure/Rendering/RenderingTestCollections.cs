namespace Incoders.Hive.Tests.Infrastructure.Rendering;

/// <summary>
/// xUnit collection that runs SkiaSharp-backed renderer tests serially (not in parallel with any
/// other test collection). SkiaSharp image/font decoding occasionally produced a flaky failure in
/// <c>LocalSvgContentRendererTests</c> under heavy parallel Release load; isolating the collection
/// removes the contention without changing what is asserted.
/// </summary>
[CollectionDefinition("SkiaRenderingSerial", DisableParallelization = true)]
public sealed class SkiaRenderingSerialCollection
{
}
