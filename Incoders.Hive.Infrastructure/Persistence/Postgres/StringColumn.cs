namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

/// <summary>
/// Helpers for writing string values into bounded <c>character varying(n)</c> columns safely.
///
/// <para>
/// Postgres rejects an over-length value with <c>22001: value too long for type character
/// varying(n)</c>, which aborts the whole <c>SaveChanges</c> and leaves the offending entity
/// tracked — poisoning every subsequent save on the same <c>DbContext</c>. For diagnostic
/// columns (error codes, status reasons) the value comes from exception messages whose length
/// is not under our control, so we MUST clamp at the persistence boundary. The full text is
/// still available in the structured logs; the column only needs a bounded, readable excerpt.
/// </para>
/// </summary>
internal static class StringColumn
{
    // Column widths for the content-generation tables (see Add_ContentGenerationSocialTables).
    public const int JobStatus = 32;
    public const int JobStatusReason = 500;
    public const int JobErrorCode = 128;
    public const int FormatKey = 64;
    public const int FormatStatus = 32;
    public const int FormatPromptVersion = 64;
    public const int FormatErrorCode = 128;
    public const int RenderedAssetUri = 2048;

    /// <summary>
    /// Returns <paramref name="value"/> unchanged when it fits, or a truncated copy whose
    /// length is exactly <paramref name="maxLength"/> (the last character is replaced by an
    /// ellipsis so callers can tell the value was clipped). <c>null</c> passes through.
    /// </summary>
    public static string? Truncate(string? value, int maxLength)
    {
        if (maxLength <= 0) return value is null ? null : string.Empty;
        if (value is null || value.Length <= maxLength) return value;
        if (maxLength == 1) return value[..1];
        return string.Concat(value.AsSpan(0, maxLength - 1), "…");
    }
}
