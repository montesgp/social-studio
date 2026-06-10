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

Use the **Apify connector UI** (Settings → Integrations → Apify → actor mappings) to add two Instagram actor entries for the profile analyzer. If you edit JSON directly, use explicit `variantKey: "profile"` entries so the profile analyzer does not collide with the single-video analyzer.

If you do edit the JSON directly, add these two objects to the existing `actor_mappings_json` array in this exact order:

```json
[
  {
    "networkKey": "instagram",
    "variantKey": "profile",
    "actorId": "get-leads~all-in-one-instagram-scraper",
    "isPrimary": true
  },
  {
    "networkKey": "instagram",
    "variantKey": "profile",
    "actorId": "apify~instagram-profile-scraper",
    "isPrimary": false
  }
]
```

> `isPrimary` controls default actor selection when present. If omitted, JSON array order is used. Keep `get-leads~all-in-one-instagram-scraper` primary unless you intentionally want the official Apify actor to run first.

| Position | Role | Actor slug | Apify actor id |
|---|---|---|---|
| `[0]` — first entry | Default actor | `get-leads~all-in-one-instagram-scraper` | `iMcuOj3FW9xLIbqET` |
| `[1]` — second entry | Backup actor (automatic fallback) | `apify~instagram-profile-scraper` | `dSCLg0C3YEZ83HzYX` |

> Array order still matters when `isPrimary` is absent. When `isPrimary` is present, the primary entry is tried first and the remaining entries are used as fallback candidates.

---

## Details

| Topic | Decision |
|---|---|
| **variantKey** | Profile Analyzer resolution uses explicit `"profile"` entries. This isolates profile scraping from the single-video Instagram actor. |
| **Actor-specific input** | `get-leads/all-in-one-instagram-scraper` receives `scrapeMode: "instagram-profile-scraper"`, `profiles[]`, `maxPostsPerProfile`, and `proxyTier: "residential"`. `apify/instagram-profile-scraper` receives `usernames[]`. Do not force one payload shape onto both actors. |
| **Automatic backup fallback** | If the default actor fails (transport error, HTTP error, empty dataset, input-validation item, or no mappable profile item), the system automatically retries with the backup actor once in the same pipeline run. No user action needed. |
| **Fallback scope** | Fallback applies ONLY to `variantKey="profile"`. The single-video scrape path is unaffected. |
| **Missing entries** | If no entry with `networkKey="instagram"` and `variantKey="profile"` exists, every profile analysis request fails immediately with HTTP 422 and code `Integration.ProfileActorNotConfigured`. No Apify call is made. |
| **Diagnostics** | Every completed run (success or failure) stores `ProviderDiagnosticsJson` with `actor_id`, `fallback_used`, `posts_received`, `posts_processed`, `degraded_post_count`, `ai_provider_key`, and `ai_model`. Inspect this field via the GET `/api/social/profile-analyzer/{id}` endpoint. |
| **Proxy** | For the get-leads actor, the adapter sends `proxyTier: "residential"` because Instagram aggressively rate-limits datacenter traffic. The official Apify profile actor has its own `usernames[]` input schema and does not use the get-leads proxy fields. |

---

## Checklist

- [ ] `actor_mappings_json` contains entry `[0]` with `networkKey="instagram"`, `variantKey="profile"`, `actorId="get-leads~all-in-one-instagram-scraper"`, and `isPrimary=true`.
- [ ] `actor_mappings_json` contains entry `[1]` with `networkKey="instagram"`, `variantKey="profile"`, `actorId="apify~instagram-profile-scraper"`, and `isPrimary=false`.
- [ ] Array order is preserved unless `isPrimary` is intentionally used to change the default actor.
- [ ] Existing single-video entries are untouched.
- [ ] Apify subscription includes RESIDENTIAL proxy credits.
- [ ] A test profile analysis completes with `status: Analyzed`.

---

## Next step

After setup, a community manager can use the **Profile Analyzer** tab in the single-analyzer UI (Perfil mode) to run an Instagram account analysis by selecting a client + own account or pasting a profile URL.
