# Profile Analysis — Operator Setup Guide

This guide tells an operator exactly what to configure so the Profile Analyzer feature works end-to-end. The feature is part of **Mundo 2** (Instagram account analysis). It runs on the existing Apify integration credential but requires two additional entries in `actor_mappings_json`.

> See also: [`ADR 0003 — profile-analysis-model`](../architecture/adr/0003-profile-analysis-model.md) and the integrations spec at [`openspec/specs/integrations/spec.md`](../../openspec/specs/integrations/spec.md#profile-analysis--actor_mappings_json-delta-int-delta-01).

---

## Quick path

1. Go to **Settings → Integrations → Apify** and open the credential editor.
2. Add two Instagram actor entries using the **Add actor** button (select network = Instagram, paste the actor slug).
3. Make sure the order is: `get-leads~all-in-one-instagram-scraper` first, `apify~instagram-profile-scraper` second.
4. Save and test the connection.
5. Run a Profile Analysis to confirm the scrape resolves without errors.

---

## Required actor_mappings_json entries

Use the **Apify connector UI** (Settings → Integrations → Apify → actor mappings) to add two Instagram actor entries. The UI stores `variantKey: "default"` automatically — no manual JSON editing is needed.

If you do edit the JSON directly, add these two objects to the existing `actor_mappings_json` array in this exact order:

```json
[
  {
    "networkKey": "instagram",
    "actorId": "get-leads~all-in-one-instagram-scraper"
  },
  {
    "networkKey": "instagram",
    "actorId": "apify~instagram-profile-scraper"
  }
]
```

> `variantKey` is optional when using the UI. The UI persists `"default"` automatically and the resolver accepts it. If you prefer an explicit override, you may set `"variantKey": "profile"` — the resolver prefers it over `"default"` when both exist.

| Position | Role | Actor slug | Apify actor id |
|---|---|---|---|
| `[0]` — first entry | Default actor | `get-leads~all-in-one-instagram-scraper` | `iMcuOj3FW9xLIbqET` |
| `[1]` — second entry | Backup actor (automatic fallback) | `apify~instagram-profile-scraper` | `dSCLg0C3YEZ83HzYX` |

> Array order matters. The system reads array position to determine which actor is default and which is backup. Do not reorder these entries.

---

## Details

| Topic | Decision |
|---|---|
| **variantKey** | The integrations UI stores `"default"` for every actor mapping. The resolver first looks for an explicit `"profile"` entry; if none exists it accepts `"default"` entries for the same network. A manual `"profile"` entry (if present) always wins over `"default"`. |
| **Automatic backup fallback** | If the default actor fails (transport error, HTTP error, empty dataset, or no `resultType=="profile"` item in the response), the system automatically retries with the backup actor once in the same pipeline run. No user action needed. |
| **Fallback scope** | Fallback applies ONLY to `variantKey="profile"`. The single-video scrape path is unaffected. |
| **Missing entries** | If no entry with `networkKey="instagram"` exists (neither `variantKey="profile"` nor `"default"`), every profile analysis request fails immediately with HTTP 422 and code `Integration.ProfileActorNotConfigured`. No Apify call is made. |
| **Diagnostics** | Every completed run (success or failure) stores `ProviderDiagnosticsJson` with `actor_id`, `fallback_used`, `posts_received`, `posts_processed`, `degraded_post_count`, `ai_provider_key`, and `ai_model`. Inspect this field via the GET `/api/social/profile-analyzer/{id}` endpoint. |
| **Proxy** | The adapter enforces RESIDENTIAL proxy (`apifyProxyGroups: ["RESIDENTIAL"]`) for all profile scrapes. Instagram blocks datacenter IPs. Ensure your Apify subscription includes residential proxy credits. |

---

## Checklist

- [ ] `actor_mappings_json` contains entry `[0]` with `networkKey="instagram"` and `actorId="get-leads~all-in-one-instagram-scraper"` (variantKey `"default"` or `"profile"`).
- [ ] `actor_mappings_json` contains entry `[1]` with `networkKey="instagram"` and `actorId="apify~instagram-profile-scraper"` (variantKey `"default"` or `"profile"`).
- [ ] Array order is preserved (default first, backup second).
- [ ] Existing single-video entries are untouched.
- [ ] Apify subscription includes RESIDENTIAL proxy credits.
- [ ] A test profile analysis completes with `status: Analyzed`.

---

## Next step

After setup, a community manager can use the **Profile Analyzer** tab in the single-analyzer UI (Perfil mode) to run an Instagram account analysis by selecting a client + own account or pasting a profile URL.
