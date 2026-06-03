# Profile Analysis — Operator Setup Guide

This guide tells an operator exactly what to configure so the Profile Analyzer feature works end-to-end. The feature is part of **Mundo 2** (Instagram account analysis). It runs on the existing Apify integration credential but requires two additional entries in `actor_mappings_json`.

> See also: [`ADR 0003 — profile-analysis-model`](../architecture/adr/0003-profile-analysis-model.md) and the integrations spec at [`openspec/specs/integrations/spec.md`](../../openspec/specs/integrations/spec.md#profile-analysis--actor_mappings_json-delta-int-delta-01).

---

## Quick path

1. Go to **Settings → Integrations → Apify** and open the credential editor.
2. Locate the `actor_mappings_json` field.
3. Add the two profile entries shown below to the existing JSON array. Do NOT remove or change existing entries.
4. Save and test the connection.
5. Run a Profile Analysis to confirm the scrape resolves without errors.

---

## Required actor_mappings_json entries

Add these two objects to the existing `actor_mappings_json` array, in this exact order:

```json
[
  {
    "networkKey": "instagram",
    "variantKey": "profile",
    "actorId": "get-leads~all-in-one-instagram-scraper"
  },
  {
    "networkKey": "instagram",
    "variantKey": "profile",
    "actorId": "apify~instagram-profile-scraper"
  }
]
```

| Position | Role | Actor slug | Apify actor id |
|---|---|---|---|
| `[0]` — first entry | Default actor | `get-leads~all-in-one-instagram-scraper` | `iMcuOj3FW9xLIbqET` |
| `[1]` — second entry | Backup actor (automatic fallback) | `apify~instagram-profile-scraper` | `dSCLg0C3YEZ83HzYX` |

> Array order matters. The system reads array position to determine which actor is default and which is backup. Do not reorder these entries.

---

## Details

| Topic | Decision |
|---|---|
| **variantKey** | Must be exactly `"profile"` (case-sensitive match). This isolates the profile actor from the single-video actor which uses no `variantKey`. |
| **Automatic backup fallback** | If the default actor fails (transport error, HTTP error, empty dataset, or no `resultType=="profile"` item in the response), the system automatically retries with the backup actor once in the same pipeline run. No user action needed. |
| **Fallback scope** | Fallback applies ONLY to `variantKey="profile"`. The single-video scrape path is unaffected. |
| **Missing entries** | If no entry with `variantKey="profile"` exists, every profile analysis request fails immediately with HTTP 422 and code `Integration.ProfileActorNotConfigured`. No Apify call is made. |
| **Diagnostics** | Every completed run (success or failure) stores `ProviderDiagnosticsJson` with `actor_id`, `fallback_used`, `posts_received`, `posts_processed`, `degraded_post_count`, `ai_provider_key`, and `ai_model`. Inspect this field via the GET `/api/social/profile-analyzer/{id}` endpoint. |
| **Proxy** | The adapter enforces RESIDENTIAL proxy (`apifyProxyGroups: ["RESIDENTIAL"]`) for all profile scrapes. Instagram blocks datacenter IPs. Ensure your Apify subscription includes residential proxy credits. |

---

## Checklist

- [ ] `actor_mappings_json` contains entry `[0]` with `variantKey="profile"` and `actorId="get-leads~all-in-one-instagram-scraper"`.
- [ ] `actor_mappings_json` contains entry `[1]` with `variantKey="profile"` and `actorId="apify~instagram-profile-scraper"`.
- [ ] Array order is preserved (default first, backup second).
- [ ] Existing single-video entries are untouched.
- [ ] Apify subscription includes RESIDENTIAL proxy credits.
- [ ] A test profile analysis completes with `status: Analyzed`.

---

## Next step

After setup, a community manager can use the **Profile Analyzer** tab in the single-analyzer UI (Perfil mode) to run an Instagram account analysis by selecting a client + own account or pasting a profile URL.
