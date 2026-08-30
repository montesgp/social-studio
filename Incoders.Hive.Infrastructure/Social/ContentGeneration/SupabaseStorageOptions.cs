using System.ComponentModel.DataAnnotations;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Configuration options for the Supabase Storage adapter.
/// Bound from configuration section <c>Supabase</c>.
/// The <see cref="ServiceRoleKey"/> MUST come from user-secrets (dev) or an environment variable (prod).
/// It MUST NOT appear in any committed file.
/// </summary>
public sealed class SupabaseStorageOptions
{
    public const string SectionName = "Supabase";

    /// <summary>Supabase project base URL, e.g. https://&lt;project-ref&gt;.supabase.co</summary>
    [Required(AllowEmptyStrings = false)]
    public string Url { get; set; } = string.Empty;

    /// <summary>Target storage bucket name, e.g. hive-social-bucket</summary>
    [Required(AllowEmptyStrings = false)]
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// Supabase service_role JWT. Bypasses RLS — confined entirely to <c>SupabaseStorageAdapter</c>.
    /// Source: user-secrets key <c>Supabase:ServiceRoleKey</c> (dev) / environment variable (prod).
    /// NEVER set in appsettings.json or any committed file.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ServiceRoleKey { get; set; } = string.Empty;
}
