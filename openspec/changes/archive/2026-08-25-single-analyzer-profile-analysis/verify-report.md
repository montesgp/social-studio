# Verify Report — single-analyzer-profile-analysis ("Mundo 2")

**Change:** `single-analyzer-profile-analysis`
**Branch:** `feat/single-analyzer-profile-analysis`
**Verified:** 2026-06-03
**Verdict:** ✅ **PASS** — 0 CRITICAL, 0 WARNING (1 found and fixed), 5 accepted SUGGESTIONs.

---

## Verification method

Focused, evidence-based verification (the heavy line-by-line agent pass was interrupted twice by session limits, so this is the orchestrator's consolidated adjudication):

- **Per-WU TDD** is the primary requirement evidence — 930 backend tests written test-first across WU-02..WU-15, all green.
- **Gates** enforce architecture/build/secrets/security as a hard contract.
- **Targeted correctness spot-checks** on the highest-risk paths (impute load-merge-write, org-scoping, duplicate-type cleanup) by reading the real code.
- **Regression** (PA-REG-01) verified by diff + schema + arch tests.

## Gate result (authoritative, HEAD)

```
pass=16 MUST  skipped=2 (frontend tests: no runner; coverage: disabled — known)
build.backend pass · build.frontend.web pass · tests.backend 930/932 (0 failed, 2 skipped)
architecture.layering pass · architecture.imports pass (no forbidden imports)
dependencies.cycles pass · secrets.scan pass · security.* pass
PASS - no MUST gate failed.
```

## Test result

- Backend suite: **930 passed / 932 total / 0 failed / 2 skipped** (pre-existing dev-only DB inspection skips).
- Mundo 1 (regression set `SingleAnalyzer*`, `ContentAnalysisProfile*`, `SocialContentAnalysis*`): **58/58 green**.
- Frontend: `tsc --noEmit` clean, ESLint no new warnings, `next build` passes (recharts lazy-loaded in the Profile module).

## Regression (PA-REG-01) — CONFIRMED

- `git diff dev -- apps/web/components/social/single-analyzer-client.tsx` → **EMPTY** (mode switch implemented as a wrapper `AnalyzerModeSwitch`, the 3360-line monolith untouched).
- Migration `20260602204600_AddProfileAnalyses.Up()` only `CreateTable("profile_analyses", schema:"social")` — `social.single_content_analyses` unchanged.
- Architecture tests assert ProfileAnalysis ↮ SingleAnalyzer namespace isolation **both directions**.

## Findings

### CRITICAL — none

### WARNING — 1 (FIXED)
- **Duplicate `GetProfileAnalysisQuery`** declared in both the `Analyze` and `History` namespaces, each with a handler; only the `History` one was live (dispatched by the endpoint + tests via explicit alias). The `Analyze`-namespace record + handler were dead code. **Removed** (commit `4ecd12f`); 154 ProfileAnaly tests + all gates green after removal.

### SUGGESTION — 5 (accepted, non-blocking)
1. **`GetClientPlanComparisonQuery` co-located** inside `GetProfileDashboardQueryHandler` rather than a standalone MediatR query (design suggested separate). Functionally equivalent; avoids MediatR overhead for an internal read. Accepted.
2. **`ProfileAnalysis.Reconstitute()` is `public`** (not `internal` + `InternalsVisibleTo`) because Infrastructure is a separate assembly; guarded by XML-doc "Infrastructure-only". Accepted; optional follow-up.
3. **`ProfileAnalysisBackgroundWorker` lives in Infrastructure** (the single-analyzer worker lives in ApiService) — deliberate, so the test project (which references Infrastructure, not ApiService) can test it. Accepted.
4. **`ProfileScrapeResult` widened** with optional `UsedActorId` / `FallbackUsed` (defaults) to carry diagnostics — backward-compatible. Accepted.
5. **ContentPlan impute create-path**: when creating a brand-new plan with only KPIs selected, the objective text falls back to the AI suggestion (a plan requires an objective). Minor over-impute on the create path only; idempotent. Accepted.

## Spec coverage (summary)

PA-01..PA-24, PA-REG-01, INT-01/02 are each exercised by named tests across WU-02..WU-15 (see `tasks.md` Spec Coverage Matrix). Spot-verified: PA-06 `resultType=="profile"` selection (adapter tests), PA-05 default→backup fallback (resolver tests), PA-07/08/09 owner-filter + degraded + video-only median (metric-calculator tests), PA-20/21/22 impute guards + load-merge-write idempotency (impute handler tests), org-scoping on every query (`GetByIdAsync(id, orgId)`), PA-15 list = **org-isolated** (matches the spec's "org isolation from principal" — user-scoping is NOT required for org-owned profile analyses).

## Operator action required (post-merge)

Configure the Apify connector `actor_mappings_json` for the org with two entries under `(networkKey:"instagram", variantKey:"profile")`, default first:
```json
[{"networkKey":"instagram","variantKey":"profile","actorId":"get-leads~all-in-one-instagram-scraper"},
 {"networkKey":"instagram","variantKey":"profile","actorId":"apify~instagram-profile-scraper"}]
```
Missing entries → clear `Integration.ProfileActorNotConfigured` 422 (no silent failure). See `docs/operators/profile-analysis-setup.md`.

## Conclusion

Implementation matches spec + design with no critical gaps. Mundo 1 is provably untouched. **Ready for the single PR (`size:exception` approved).**
