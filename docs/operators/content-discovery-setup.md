# Content Discovery — Operator Setup Guide

Content Discovery finds social content that is already performing, scores it, and proposes ranked
candidates. The operator picks one and analyses it through the existing Single Analyzer pipeline.

> See also: [`openspec/changes/content-discovery-intelligence/`](../../openspec/changes/content-discovery-intelligence/)
> and [ADR-0012](../architecture/adr/0012-content-discovery-source-chain-and-candidate-persistence.md).

---

## At a glance

| Topic | Value |
|---|---|
| Where it lives | Single Analyzer → **By Search** tab |
| Workflow key / analysis type | `single-analyzer` / `content-discovery` |
| Routable step | `discover-niche-content` (actor variant `niche-search`) |
| Sources | Reference accounts, client own accounts, TikTok niche search |
| Networks configurable | Instagram, TikTok, YouTube |
| Networks with a working search adapter | **TikTok only** — the others are configurable but not yet implemented |
| Processing model | Instant, single selection, screen-blocking progress overlay |
| Actor selection | Always from configuration. The platform never falls back to a hardcoded actor |

---

## Part 1 — Wire the Apify actor (operator, one time)

This is **three distinct steps**. Doing only the first is the most common mistake: syncing does not
route anything.

### Step 1 — Sync the actor catalog

1. Go to **Settings → Integrations**.
2. On the **Apify** card (status must be **Connected**), click **Sync actors**.
3. A toast reports how many actors came back.

The sync lists every actor your Apify account **created or used** — both your own actors and public
store actors you have run. It deliberately does *not* filter to owned actors only: the Single
Analyzer's existing routing depends on public store actors, and filtering them out would break it.

Syncing only fills a catalog. It makes actors *selectable*; it does not decide what they are for.

### Step 2 — Map the actor in the Apify connector

1. On the **Apify** card, open **Configure**.
2. In the actor routing section, add a row:
   - **Network** — e.g. TikTok
   - **Area** — `Búsqueda por nicho` for discovery (`Análisis de contenido` and `Análisis de perfil`
     are the pre-existing areas)
   - **Actor** — pick from the dropdown, now populated by the sync. Choose *Otro… (ID manual)* to
     enter an actor the catalog does not have.
   - **Principal** — tick it for the primary route; untick for fallbacks.
3. Save.

The mapping is what tells the platform *which actor serves which network and area*. This is the part
that cannot be derived from Apify.

### Step 3 — Route the workflow step

1. Go to **Settings → Integrations**, workflow selector → **Single Analyzer**.
2. Find the **Content discovery** analysis type → step **Discover niche content candidates**.
3. Pick the network and the actor. The actor dropdown offers exactly the mappings whose network and
   area match this step.
4. Save.

> If the actor dropdown is empty and shows *"Configure actor mappings in the Apify connector"*, step 2
> is missing for that exact **network + area** pair. A mapping on Instagram will not populate a step
> routed to TikTok.

For an organization with a connected Apify integration, the step row is pre-seeded — disabled and
with no actor — so it appears ready to configure. If it is missing entirely, use **Seed catalog
stages** in the workflow section.

---

## Part 2 — Running a discovery

1. Open **Single Analyzer** → **By Search** tab.
2. Pick the client, the search criterion and the network.
3. Click **Discover**.
4. Ranked candidates render. Select **exactly one**.
5. Click **Analyze this one**. The existing screen-blocking overlay takes over (Queued → Fetching
   content → Content ready → AI analysis) and from there the flow is identical to analysing a URL.

Selecting several candidates for background batch processing is **not** part of this flow — that is
the Magic Generator, a separate future capability.

### Search criteria

| Criterion | Source | Requires an Apify actor? |
|---|---|---|
| Reference accounts | The client's configured referent accounts | No — reuses profile scraping |
| Own accounts | The client's own accounts | No — reuses profile scraping |
| Niche search | Keyword/hashtag search on the network | **Yes** |
| Viral / Competitors | Fall back to the default chain | No |

---

## Part 3 — Reading the results

### Unconfigured vs empty

These are different outcomes and the UI distinguishes them deliberately:

- **Unconfigured** — the stage has no enabled actor for the requested network. The run stops for that
  source and points you at settings. This is the expected behaviour when step 2 or 3 is missing.
- **Empty** — the stage ran and genuinely found nothing.

A source that fails does not empty the whole run: the remaining sources still contribute, and the
failure is reported rather than swallowed.

### Trending vs Proven

- **Trending** — recent, with velocity signals.
- **Proven** — demonstrated consistent performance, not necessarily recent.
- **Undetermined** — not enough signal to classify.

Classification is derived from a **single snapshot**. True growth velocity requires observing the
same candidate twice; the candidate table makes that possible in a future iteration, but it is not
measured today. The current proxy is views per day derived from the publish date.

### Relative performance

A candidate is scored against the publishing account's own baseline where one is available — 200k
views on an account that normally does 10k outranks 1M on an account that normally does 5M.

Baselines are computed only for **already-known accounts** (referents and own accounts, which are
already scraped) and for the **top candidates** after cheap filtering. They are never computed for
every candidate: that would cost one extra scrape per author across the whole discovery set.

When no baseline exists the score degrades gracefully — it is never fabricated.

### Metrics that a network does not report

Networks expose different metrics: TikTok reports shares, Instagram does not report them the same
way, YouTube reports neither shares nor saves. A metric the network does not report is stored as
*absent*, not as zero, and drops out of the score entirely. Treating it as zero would systematically
penalise whole networks.

---

## Known limitations

| Limitation | Detail |
|---|---|
| Niche search adapter unverified | The TikTok niche-search response mapping was written against Apify's documented item shape, not a verified live call. After the first real run, corrections belong in `TikTokNicheSearchAdapter.MapItem` — the mapping is isolated there on purpose. |
| Instagram / YouTube niche search | Configurable but not implemented. They degrade through the unconfigured path rather than failing. |
| Clustering | Cross-platform pattern clustering is not implemented. Pre-analysis contributes only a lexical co-occurrence signal to the score. |
| Candidate table growth | `discovered_candidates` is append-only and unbounded in this iteration. An index supports a ranged purge later. |
| Tuning constants | The Proven multiplier and the trending views-per-day proxy are defaults chosen during implementation, not product-specified values. They decide what ranks highest and are worth reviewing against real data. |

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| No **Sync actors** button | The Apify integration is not `Connected`, or the API was not restarted after deployment | Connect the integration; restart the API |
| Sync fails with a database error | The actor catalog migration has not been applied | Restart the API — migrations run at startup |
| Actor dropdown empty on the workflow step | No connector mapping for that network + area pair | Complete Part 1, step 2 |
| Route shows a raw id like `Apify · DdSkpbFy5T8FyCsor` | The actor is not in the synced catalog | Run **Sync actors**; the label falls back to the id until then |
| **Content discovery** missing from the workflow | The seeded step row does not exist for this organization | Use **Seed catalog stages** in the workflow section |
