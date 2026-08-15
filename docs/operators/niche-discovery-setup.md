# Niche Discovery — Operator Setup Guide

Niche Discovery finds **market opportunities to build new channels around** — no client is involved.
It is the market-research step before a channel launch, not a content-generation tool. If you are
looking for content to generate FROM for an existing client, see
[`content-discovery-setup.md`](content-discovery-setup.md) instead — the two capabilities share the
word "discovery" and nothing else.

> See also: [`openspec/changes/niche-discovery-engine/`](../../openspec/changes/niche-discovery-engine/)
> and [ADR-0013](../architecture/adr/0013-niche-discovery-multi-origin-provider-and-opportunity-evidence-model.md).

---

## At a glance

| Topic | Value |
|---|---|
| Where it lives | Settings → Integrations → **Niche Discovery** checklist group |
| Workflow key / analysis type | `niche-discovery` / `market-opportunity` |
| Provider today | YouTube Data API v3 (Apify is designed for as a second origin, not shipped yet) |
| Provider category | `OfficialDataApi` — a single API-key credential, not an actor mapping |
| Processing model | **Start + poll**, same idiom as Profile Analysis: the run executes on the server, the screen polls status every ~2.5 s |
| Market selector | **Data-driven.** Every market comes from the seeded catalog (`GET /api/social/niche-discovery/markets`) — never a hardcoded list |
| The two scores | OpportunityScore and ConfidenceScore — always shown together, never averaged |

---

## Part 1 — Get a YouTube Data API v3 key (one time, per Google Cloud project)

1. In the [Google Cloud Console](https://console.cloud.google.com/), create or select a project.
2. Enable the **YouTube Data API v3** API for that project (APIs & Services → Library → search
   "YouTube Data API v3" → Enable).
3. Create an API key (APIs & Services → Credentials → Create credentials → API key). Restricting the
   key to the YouTube Data API v3 is recommended but not required by the platform.
4. Copy the key — you will paste it once, in the next step.

The platform never falls back to a shared or hardcoded key. Without a connected key, the origin
reports as **Unconfigured**, distinct from a run that executed and found nothing.

## Part 2 — Connect the key in the platform

1. Go to **Settings → Integrations**.
2. Find the **YouTube Data API v3** card (category: Official Data API) and click **Connect**.
3. Paste the API key from Part 1 and save. The card moves to **Connected** once the key validates.
4. Under the **Niche Discovery** checklist group, confirm both items complete:
   - *Connect a YouTube Data API key*
   - *Enable a discovery origin for channel search*

If either item stays incomplete after connecting the key, the workflow step for channel search is
not routed — check that the `niche-discovery` workflow's steps are seeded (mirrors the **Seed catalog
stages** action documented for Content Discovery).

---

## Part 3 — Depth and quota tradeoffs

Each run consumes YouTube Data API v3 **quota units** (a daily budget your Google Cloud project owns,
independent of the platform). Depth controls the ceiling per run:

| Depth | Quota ceiling | AI completions ceiling | When to use it |
|---|---|---|---|
| **Quick** | 400 units | 5 | A fast first pass on a niche/market combination — cheapest, narrowest evidence |
| **Standard** | 1,000 units | 15 | The default — balances coverage against quota spend |
| **Deep** | 2,500 units | 40 | Wide coverage for a niche you are seriously evaluating; spends the most quota and AI budget per run |

A search call costs more quota than a batched detail call, which is why the origin adapter
searches sparingly and batches channel/video detail lookups rather than issuing one search per item.
If a run exhausts its provider quota with zero usable results, it reports **Failed**
(`provider_quota_exhausted`); if it exhausts quota after already gathering usable results, it reports
**PartiallyCompleted** — the run is not discarded, and `ConfidenceScore` already reflects the smaller
sample. A quota ceiling is per-run, not per-day: your Google Cloud project's own daily quota is the
outer limit across every run you start.

---

## Part 4 — Reading Opportunity vs Confidence

Every opportunity carries **two independent numbers**, always rendered side by side:

| Score | What it measures | What moves it |
|---|---|---|
| **OpportunityScore** | How strong the opportunity looks | Viral performance, cross-channel validation, demand momentum, target-market gap, automation suitability, evergreen potential, monetization potential |
| **ConfidenceScore** | How much evidence backs that score | Channel/video counts, sample size, timespan, source count, data freshness, channel diversity, signal consistency |

They are **never averaged into one number** — a 94/37 opportunity (strong signal, thin evidence) is a
fundamentally different bet than an 88/95 one (solid signal, well-evidenced), and collapsing them
would hide that difference from you.

**CrossPlatformValidation** has no data source in this release. It is never scored as zero — it is
excluded from the weighted sum, and the remaining weights are renormalized to sum to 1.0. The
dashboard and detail view both call this out explicitly (an amber note naming the excluded signal and
the renormalized denominator) so you know the score was computed from fewer signals, honestly, rather
than silently.

A metric the source network does not report (e.g. a channel with a hidden subscriber count) is shown
as **unavailable**, never as `0` — a real zero and a missing measurement never look the same on
screen.

---

## Part 5 — Reading a run's terminal status

| Status | What it means | What to do |
|---|---|---|
| `Completed` | The run finished and every configured origin executed cleanly (it may still have found zero opportunities — that is a legitimate outcome, not a failure) | Review the ranked opportunities, or accept that this niche/market combination came back empty |
| `PartiallyCompleted` | The run produced opportunities, but something degraded along the way: an origin was unconfigured or failed mid-run, or AI classification fell back to lexical clustering | Read `unconfiguredSourceKeys`/`failedSourceKeys` on the run status; the opportunities are still real, just narrower evidence than a fully clean run |
| `Failed` | The run produced **zero** usable evidence: either no origin was configured for the requested network, or every configured origin failed/exhausted quota | Check `failureReasonKey` — `origin_unconfigured` means Part 1/2 above is incomplete; `provider_quota_exhausted`/`provider_unavailable` means retry later or raise your Google Cloud project's quota |

`Failed` and "ran, found nothing" are never the same status — a completed run with zero opportunities
is `Completed`, not `Failed`. This distinction exists specifically so a genuinely empty niche is never
confused with a broken integration.

---

## Known limitations

| Limitation | Detail |
|---|---|
| Apify origin | Designed for (the port supports multiple configuration-selected origins per `DiscoveryStage`) but not implemented in this release. YouTube Data API v3 is the only shipped origin. |
| No cross-run comparison view | Comparing two runs today means opening the dashboard for each run's id. There is no dedicated "compare markets" screen in this release. |
| No operator action on an opportunity | There is no pursue/dismiss/track state. Comparison over time is done by re-running discovery and comparing runs, not by tracking a single opportunity's status. |
| Content Generator hand-off | A seam (`INicheOpportunityActivationSource`) is named in the codebase for a future change to consume an opportunity into content generation. It ships with no implementation and no registration in this release. |
