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
| Processing model | **Start + poll**: the scrape runs on the server, the screen polls it. Single selection, screen-blocking progress overlay |
| Actor selection | Always from configuration. The platform never falls back to a hardcoded actor |
| Where candidates come from | The **candidate bank** first. A billed run happens only when the bank is empty for that niche, or you click **Refresh (billed run)** |
| Bank scope | **Organization + network + hashtag.** Never per client — two clients in the same niche share one paid scrape |
| Bank refresh | **Manual only.** No expiry, no automatic re-scrape |

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

> **Step 3 is optional if step 2 is done.** Niche search resolves an actor from the workflow step route
> first and falls back to the connector mapping (step 2). The readiness signal follows exactly the same
> two sources, so an actor mapped in the connector alone counts as configured — the checklist and the
> runtime no longer disagree.

### What the readiness signal says per network

The integrations checklist carries one **optional** item per network that has a working niche-search
adapter — today that is TikTok only.

| Network | What the operator sees |
|---|---|
| TikTok, actor resolved (route or connector mapping) | *Configure a TikTok niche-search actor* — **completed**. The By Search → Niche stage runs. |
| TikTok, nothing resolved | *Configure a TikTok niche-search actor* — **not completed**, plus a By Search banner: *"TikTok niche search isn't configured yet. Map or route a niche-search actor for it under Settings / Integrations."* with a link to settings. |
| Instagram / YouTube | **No checklist item at all.** The By Search banner reads *"… niche search isn't supported yet — only TikTok has a working niche-search adapter today."* and offers no settings link, because no configuration would make it work. |

---

## Part 2 — The candidate bank

**Discovery does not re-scrape.** Every candidate a niche search brings back is stored in a
**candidate bank**, and the next Discover is served from that bank instead of dispatching another paid
Apify run.

This exists because the opposite happened: the tables were built and nothing ever wrote to or read
from them, so every Discover paid for data the platform already had. Five consecutive runs (202, 134,
120, 55 and 55 items) bought the same niche over and over.

### What is stored, and under what key

| | |
|---|---|
| Scope | **Organization + network + hashtag** |
| NOT scoped by | **Client.** Two clients of the same organization in the same niche share the reserve |
| Hashtag key | The hashtags actually searched — the **curated** niche hashtags, or the **derived** tokens for a client with no niche assigned (see *How a client's niche becomes hashtags*). Never the raw niche phrase |
| Duplicates | A candidate already in the bank is never stored twice — the same `CandidateIdentity` the shortlist dedups with |
| Rewrites | None. A candidate already banked keeps its original observation, so the age you are shown stays true |

Because the key is the hashtag list itself, clients that share a **catalog niche** converge exactly:
two clients assigned to *Medical & Health* both key on `medical`, `health`, so the second one is served
from the reserve the first one paid for — regardless of how their sector text is worded. For clients
still on the legacy free-text path the same convergence happens where the derived tokens overlap:
*"Medical & health"* derives `medical`, `health`, `medicalhealth`, and a client whose niche is simply
*"Health"* matches on `health`. When the same candidate is found again under a new wording, the
wordings it is reachable under are widened rather than the candidate being stored a second time.

### When a billed run actually happens

Exactly two cases, and nothing else:

1. **The bank is empty** for that organization + network + derived hashtags.
2. **You clicked Refresh (billed run)** — an explicit opt-in.

There is **no time-based expiry** and **no automatic re-scrape** when the pool is thin. Nothing in the
platform can start a paid Apify run without one of the two conditions above.

### Telling stored results from fresh ones

The shortlist always says where its candidates came from:

| Indicator | Meaning |
|---|---|
| **From the stored bank** *(with an age: "stored today", "1 day old", "30 days old")* | Served from the reserve. No provider call, nothing billed. The age is that of the **oldest** candidate in the shortlist — staleness is judged by the worst case |
| **Fresh scrape** | This run scraped the network, and it was billed |
| *(no indicator)* | The session did not report an origin — this is never guessed as "fresh" |

### The Refresh button

**Refresh (billed run)** sits next to **Discover** and is deliberately not a second Discover: it is the
only control on the By Search tab that can spend money. It is offered **only for the niche criterion**
— reference and own accounts reuse the profile scraping you already configured and have no billed
niche run to refresh — and it is disabled with a stated reason rather than silently.

### Seeding the bank locally from captured runs (Development only)

Several early Apify runs were paid for and their output discarded, which left the bank empty and every
tool built on it — the shortlist, the AI Analysis page — with nothing to show. The output of those runs
was captured and committed, and a startup importer replays it into the bank so local work has real
candidates without buying another run.

| | |
|---|---|
| Where the files live | `Incoders.Hive.ApiService/seed-data/discovery/*.json` |
| What they contain | One envelope per niche: `networkKey`, `hashtags`, `sourceNote`, and `items` — the **verbatim** items a real Apify run returned |
| How they are mapped | Through `TikTokNicheSearchAdapter`, the same class a live run uses. There is no seed-specific parser: if the production mapping regresses, the import stops producing candidates and says so |
| Where they land | `RecordNicheBankAsync(organizationId, "tiktok", hashtags)` — exactly the key a live run would have used |
| Re-running | Safe. Dedup is the same `CandidateIdentity` the discovery chain uses, so restarting the API imports nothing new. The log reports new versus already present, per file |
| A malformed file | Logged as an error and skipped; the other files still import and startup is never taken down by seed data |

**It is off by default and Development-only.** Two independent switches must both hold before a single
row is written, so neither an environment mistake nor a stray config flag can fill a real tenant's bank
with somebody else's captured niche.

`appsettings.Development.json` in this repository already carries it, pointed at the **Acme Studio**
organization, so a fresh local API starts with real candidates instead of an empty bank. Change the
`OrganizationId` if your local database uses a different tenant, or set `Enabled` to `false` to opt out.
Production is untouched: neither switch is set outside the Development file, and the importer refuses to
run outside Development regardless.

Enable it in `Incoders.Hive.ApiService/appsettings.Development.json`:

```json
"Social": {
  "DiscoverySeedImport": {
    "Enabled": true,
    "OrganizationId": "00000000-0000-0000-0000-000000000000"
  }
}
```

| Setting | Meaning |
|---|---|
| `Enabled` | Master switch. Default `false` — the importer ships wired but inert |
| `OrganizationId` | Which bank receives the seed. Optional: when absent, the importer uses the single active organization **only if exactly one exists**. With zero or several it logs a warning and imports nothing rather than guessing or fanning the same rows across tenants |
| `DirectoryPath` | Default `seed-data/discovery`, resolved against the API content root and then the application base directory |
| `AllowOutsideDevelopment` | Default `false`. The importer is inert outside Development whatever `Enabled` says. Only set this on a throwaway host that really should carry test data |

Restart the API, then check the log for one `Discovery seed file '<name>.json': N new, M already present …`
line per file. The candidates appear on the By Search niche shortlist and in AI Analysis for that
organization — served from the bank, so no run is dispatched. A client whose sector/niche derives none
of the seeded hashtags will still see nothing: the bank key is the hashtag set, so the client's niche
has to overlap it (see *How a client's niche becomes hashtags*).

---

## Part 3 — Running a discovery

1. Open **Single Analyzer** → **By Search** tab.
2. Pick the client, the search criterion and the network. **TikTok is the default**, because it is the
   only network whose niche search can actually run.
3. Click **Discover**. A screen-blocking progress overlay appears immediately — the same overlay the
   analysis flow uses — with one row per network plus the analysis and finalize rows. It is driven by
   the server's own session status, refreshed every 2.5 s.
4. When the session finishes you get exactly one of four answers (see *How a discovery ends* below).
   On success, ranked candidates render. Select **exactly one**.
5. Click **Analyze this one**. The existing screen-blocking overlay takes over (Queued → Fetching
   content → Content ready → AI analysis) and from there the flow is identical to analysing a URL.

Selecting several candidates for background batch processing is **not** part of this flow — that is
the Magic Generator, a separate future capability.

### How the discovery actually executes

**Discovery does not run inside the HTTP request any more.** Clicking **Discover** POSTs to
`/api/social/workflows/content-generation`, which validates the input, opens a session and returns a
workflow id in milliseconds. The scrape then runs on a **server-side background task**, and the screen
polls `GET /api/social/workflows/{id}/status` until the session reports a terminal state.

This matters because a real scrape does not fit in one HTTP request. Before this change the request
blocked on the whole scrape and was aborted by the browser or a proxy at about 30 seconds
(`Niche search CANCELLED BY THE CALLER after 29965 ms`), while the Apify run **kept going and kept
billing**. No timeout value would have fixed that: the execution model was wrong for the workload.

Consequences for you as an operator:

- **Navigating away no longer kills the run and no longer orphans it.** The background task ignores
  the browser's request token entirely; only the API process shutting down stops it. Come back, and
  the session is still there.
- **A proxy or gateway timeout is no longer possible on the start call**, because the start call no
  longer waits for anything.
- **Sessions are held in memory and are lost when the API restarts.** A poll for a workflow id this
  process never saw answers a clean *not found* — it does not error. If the API restarts mid-run, run
  the discovery again.
- The screen gives up polling after 5 minutes and tells you the run is *still going on the server*
  rather than pretending it failed.

### How a discovery ends

Four terminal states, deliberately kept apart because each has a different fix:

| Session status | What the screen shows | What it means | What to do |
|---|---|---|---|
| `completed`, candidates > 0 | The ranked shortlist | The chain found content | Pick one and analyse it |
| `completed`, candidates = 0 | *"Discovery ran and found no candidates for this client and criterion."* | The chain ran and genuinely found nothing | Widen the criterion, or check the client's referents/niche |
| `failed` | *"Discovery could not be completed — the provider could not be reached or rejected the run."* | Every source that ran errored (provider down, credentials, quota) | Read the niche-search log rows below. **Do not** change the search criterion — it is not the cause |
| `unconfigured` | *"Discovery is not configured for the selected network."* plus a **Go to Settings / Integrations** button | No enabled actor is routed or mapped for that network | Complete Part 1, step 2 (and optionally step 3) |

A provider failure never renders as "no candidates" any more. That conflation used to send operators
rewriting a perfectly good niche while the real problem was upstream.

A run that produced candidates stays `completed` even if one of its sources failed or was
unconfigured — one bad source never voids a run that found something.

### Search criteria

| Criterion | Source | Requires an Apify actor? |
|---|---|---|
| Reference accounts | The client's configured referent accounts | No — reuses profile scraping |
| Own accounts | The client's own accounts | No — reuses profile scraping |
| Niche search | Keyword/hashtag search on the network | **Yes** |
| Viral / Competitors | Fall back to the default chain | No |

---

## Part 4 — Reading the results

### How a client's niche becomes hashtags

**The curated catalog decides.** A client is assigned to one or more niches from the platform niche
catalog (see [Niche / Sector Catalog](niche-catalog.md)), and each niche carries a **curated hashtag
list**. What a niche search asks the network for is the **union of the curated hashtags of every niche
the client is assigned to**, de-duplicated and in assignment order. Nothing is guessed from spelling.

| The client is… | What is searched |
|---|---|
| Assigned to one or more niches with curated hashtags | The **union** of those curated hashtags, de-duplicated, capped at **5** |
| Assigned to **no** niche | Legacy fallback: the hashtags **derived** from the free-text sector (below) |
| Assigned to niches that **all** have an empty curated list | **Nothing.** The niche stage reports **Unconfigured** and no provider run is dispatched |

A client assigned to *Medical & Health* searches `medical` and `health` — exactly what a sysadmin
curated. It no longer searches `medicalhealth`, the tag that does not exist on TikTok and that the
free-text derivation used to produce.

Because two clients of the same organization on the same niche now produce the **same** hashtag list,
they share the same candidate-bank key **by construction**: the second client's run reads the bank the
first one paid for, whatever their sector text happens to say.

**When the union is capped:** a client assigned to many niches can accumulate more than 5 curated
hashtags. Only the first 5 (in assignment order) are searched, and a warning names both the kept and
the dropped tags — *"only the first 5 are searched … the rest are dropped"*. Remove niches from the
client to change which ones travel.

**When a niche has no curated hashtags:** the run does **not** fall back to the free text. Falling
back would quietly resurrect the guessed hashtags the catalog exists to replace, so instead the niche
stage reports Unconfigured and the log names the client. Fix it by curating the hashtags of those
catalog entries.

#### Legacy fallback — deriving hashtags from free text

This path applies **only** to a client with no catalog niche assigned. The old niche field is **free
text** — *"Medical & health"*, *"Salud & Nutrición"*, *"e-commerce"*. A network hashtag is not: it
carries no spaces, no accents and no punctuation. So the phrase is **derived** into a small set of
hashtag candidates before anything is sent:

1. Lowercase, strip diacritics (`nutrición` → `nutricion`), remove every character that is not a letter
   or a digit — which also removes a `#` you may have typed yourself.
2. Split on whitespace and drop connector words in English and Spanish (`and`, `or`, `the`, `of`, `for`,
   `y`, `e`, `o`, `de`, `del`, `la`, `el`, `los`, `las`, `en`, `para`, `con`) and bare `&`.
3. Emit each surviving token **plus** the full concatenation of the phrase, deduplicated and in order.
   Tokens of a single character are not emitted on their own, but still belong to the concatenation.
4. Cap the list at **5** hashtags, so one long niche phrase cannot fan a single search out into an
   expensive provider run.

| Legacy free-text niche (no catalog niche assigned) | Hashtags actually searched |
|---|---|
| `Medical & health` | `medical`, `health`, `medicalhealth` |
| `Salud & Nutrición` | `salud`, `nutricion`, `saludnutricion` |
| `content strategy` | `content`, `strategy`, `contentstrategy` |
| `#fitness` | `fitness` |
| `the and of` | *(none — see below)* |

All candidates travel in **one** call, because the actor's `hashtags` input is an array and we cannot
know in advance which form actually exists on the network. `#medical` and `#medicalhealth` are both
plausible; asking for both costs one call, guessing costs a wasted run.

### How the result budget is split across the hashtags

The actor applies `resultsPerPage` to **each** hashtag in the array, so the number of results you ask
for is a **total** that gets divided, never a per-hashtag figure.

| Requested (`MaxResults`) | Derived hashtags | `resultsPerPage` sent | Videos the actor may scrape |
|---|---|---|---|
| 72 | 3 (`medical`, `health`, `medicalhealth`) | **24** | up to 72 |
| 72 | 1 (`fitness`) | **72** | up to 72 |
| 2 | 3 | **2** (never 0) | up to 6 |

The arithmetic is a ceiling division (`ceil(72 / 3) = 24`), floored so a many-way split never asks for
zero results, and capped at 100 per input. Whatever comes back, the shortlist is still truncated to the
`MaxResults` the caller asked for — that number is the upper bound of the whole call.

Before this split, `72` was sent **per hashtag**: a 3-hashtag niche asked one synchronous call for up to
**216** scraped videos. That both overran the 120 s request timeout and billed roughly three times the
intended run. The `searchQueries` reshape retry sends a single term, so it keeps the whole budget.

If the derivation produces nothing at all (a blank niche, punctuation only, or nothing but connector
words), **no actor is called**. The run reports a failure and the log names the offending search term.
Fix it by giving the client a niche with at least one real word.

### Cost and duration guards on every run

**The Apify dashboard's saved actor input does NOT apply to an API run.** Setting "Max posts per
hashtag = 5" in the dashboard changes nothing for us: the run endpoint takes its input from the body we
POST, and any field we leave out falls back to the *actor's schema default*, never to what you saved in
the UI. Real runs proved it — with `5` saved in the dashboard, runs came back with **202** and **120**
items.

Worse, the per-page field we send (`resultsPerPage`) is not necessarily the field *your* actor uses for
its per-hashtag cap. When the name does not match, the actor silently ignores our value. We cannot know
the right name in advance, because the actor is yours to choose.

So the real limits are applied at the **platform** level, as query parameters on the run URL. They hold
whatever the actor's input schema is, and whatever it does with the body:

| Guard | Config key | Default | What it does |
|---|---|---|---|
| `maxTotalChargeUsd` | `Apify:NicheSearchGuards:MaxTotalChargeUsd` | **0.25** USD | Caps the **total cost** of one run across *all* Apify pricing models. This is the money guard — it works regardless of how the actor is priced or how many items it decides to scrape. |
| `timeout` | `Apify:NicheSearchGuards:RunTimeoutSeconds` | **90** s | Hard run duration limit, enforced by Apify. Deliberately below our own 120 s request timeout, so the platform kills the run *before* we stop waiting. |
| `maxItems` | `Apify:NicheSearchGuards:MaxItems` | **50** | Caps how many dataset items are **charged for** on pay-per-result actors. Apify is explicit that this does *not* guarantee the actor returns only that many items, so treat it as a billing cap, never as a hard result limit. |

The resulting run URL looks like this (the token is always masked in the log):

```
https://api.apify.com/v2/acts/<actorId>/run-sync-get-dataset-items?token=***&maxTotalChargeUsd=0.25&timeout=90&maxItems=50
```

**Tuning them.** Put the keys above in `appsettings.json` (or the matching environment variables, e.g.
`Apify__NicheSearchGuards__MaxTotalChargeUsd`). The defaults are chosen for a test environment where a
runaway run has already cost real money; raise them only once you have measured what a normal run of
*your* actor actually costs. Setting a guard to `0` or a negative number omits it from the URL — that
disables it, and is not recommended.

**An aborted request still bills.** If the browser, a proxy or a gateway aborts the HTTP request, the
Apify run is **not** cancelled — it keeps running and keeps billing. Ten such runs accumulated in
production before these guards existed. The `timeout` guard is now what bounds that case: an orphaned
run stops at 90 s instead of grinding on unattended.

Since discovery moved to start + poll, the **browser can no longer cause this at all**: the scrape
runs on a background task that never observes the request's cancellation token, so closing the tab or
navigating away neither kills the run nor orphans it. The guards above still apply — they are the
platform's cost ceiling for every run, aborted or not.

**When the actor ignores our limit field**, the log says so — see the
`RETURNED MORE ITEMS THAN REQUESTED` row below. If you see it repeatedly for the same actor, the fix is
a per-actor input-field mapping (so the actor's own per-hashtag cap is set correctly); until then the
platform guards above are what makes the unknown field name affordable.

### Input shapes and the one reshape retry

Apify's TikTok actors do not agree on an input schema: hashtag/"Discover"-style actors expect
`{ "hashtags": ["software"] }`, keyword scrapers expect `{ "searchQueries": ["software"] }`. Which one
your actor wants is a property of the actor you chose, and it cannot be known in advance.

So the run does this, per configured actor:

1. Sends the **hashtag** shape first (with every `shouldDownload*` flag set to `false` — the platform
   reads metadata only and never pays for media downloads), carrying every hashtag derived from the
   niche as described above.
2. If — and only if — Apify answers **HTTP 400** (the actor rejecting that body), the **same actor** is
   retried **once** with the `searchQueries` shape. Both attempts are logged, each naming its
   `InputShape`.
3. `401` / `403` (credentials), `429` (quota) and `5xx` (outage) are never reshaped — those failures say
   nothing about the request body. The run moves on to the next configured actor.

If both shapes are rejected, the next configured actor (a non-primary connector mapping, if any) is
tried from step 1.

### Response item shapes and mapped fields

Actors also disagree on how they shape each returned item. Some answer nested objects
(`"authorMeta": { "name": "…" }`); the "Discover"-style actor most operators map answers **flattened**
dot-notation keys instead (`"authorMeta.name": "…"` is a single field whose name contains a dot). Both
shapes are read, so the actor you picked does not change what gets mapped.

| Actor field (either shape) | Candidate property | Absent → |
|---|---|---|
| `webVideoUrl` | Content URL | item **skipped** — never an invented URL |
| `text` | Title (first line, trimmed to 140 chars) and summary | title falls back to *"Video de \<handle\>"* |
| `authorMeta.nickName`, else `authorMeta.name` | Source handle — the join key for the account baseline | item **skipped** |
| `playCount` / `diggCount` / `commentCount` / `shareCount` | Views / Likes / Comments / Shares | `null` (absent, not zero) |
| `collectCount` | Saves | `null` — the Discover actor does not report saves at all |
| `createTimeISO` | Published at (UTC) | `null` |
| `videoMeta.coverUrl`, else `videoMeta.originalCoverUrl`, else `covers.default` | Thumbnail | `null` — see *Known limitations* |

Fields the actor returns that the shortlist does not use (`videoMeta.duration`, `musicMeta.*`,
`authorMeta.avatar`) are ignored rather than repurposed.

### When a hashtag does not exist

A hashtag that no longer exists — or never did — is **not** an HTTP error. The actor answers `200 OK`
with an *error item* in the dataset:

```json
[{ "error": "This profile/hashtag does not exist.", "errorCode": "NOT_FOUND", "input": "medicalhealth" }]
```

These items are recognised as **provider-reported errors**, never as items the mapping failed to read,
and the log says so explicitly, naming the `errorCode` and the offending `input`. Nothing about the
actor, the routing or the item mapping is wrong when you see this.

- **Some hashtags resolved, others did not** — the run **succeeds** with whatever mapped. A single dead
  hashtag never voids the whole search; the log records the dead ones at information level.
- **No hashtag resolved** — the run fails, and the message reads *"found no hashtag for the search
  term …"*, not *"the response shape does not match"*.

What to do as an operator: change the client's niche so it derives a tag people actually use. Check on
the network itself which of the derived candidates has content (`#medical` almost certainly does,
`#medicalhealth` may not) and adjust the configured niche accordingly. Do **not** re-map the actor —
this failure says nothing about it.

### Reading the niche-search log

Every niche-search call writes a `START` line and then **exactly one** terminal line, always carrying
the actor id, the input shape and the elapsed milliseconds. If you see a `START` with no terminal line,
the API process itself died — that is the only remaining explanation.

| Log line | What happened | What to do |
|---|---|---|
| `Niche search START` | The call left, with the request size and a body preview | Wait for the terminal line |
| `Niche search SUCCESS after N ms … ItemCount=X` | The actor answered; `X` is how many raw items came back | Nothing — the run worked |
| `Niche search RETURNED MORE ITEMS THAN REQUESTED … ItemCount=X RequestedBudget=Y` | The actor ignored the per-page limit field we send and scraped far more than we asked for | Cost was still capped by the run guards. If it repeats for the same actor, a per-actor input-field mapping is needed |
| `Niche search TIMED OUT after N ms` | Our own 120 s per-request HttpClient timeout fired. The actor was still running; we stopped waiting | Narrow the client's niche (fewer derived hashtags) or lower the requested result count |
| `Niche search CANCELLED BY THE CALLER after N ms` | Something aborted the call before the actor answered. Since discovery moved to start + poll this can no longer be caused by the browser or a proxy — the only remaining cause is the API process shutting down mid-run | Check the API host/container for a restart or deployment |
| `Niche search TRANSPORT FAILURE after N ms` | Apify could not be reached at all (DNS, TLS, network) | Check connectivity and the Apify status page |
| `Niche search RESPONSE ERROR after N ms … HTTP <code>` | Apify answered with an error status | See the input-shape and troubleshooting rules above |
| `Niche search NON-JSON RESPONSE after N ms` | A 200 whose body is not JSON | Read `ResponsePreview`; usually a proxy or error page in front of Apify |
| `Niche search UNEXPECTED FAILURE after N ms` | Anything else. Logged at **error** level with the exception type | Report it — this line exists so no failure is silent |

The elapsed milliseconds are on every terminal line on purpose: a value close to `120000` says the
120 s timeout was reached, which is a *sizing* problem, not a configuration one.

### Unconfigured vs empty vs failed

These are three different outcomes and the UI distinguishes all three deliberately — see
*How a discovery ends* above for what the screen says for each:

- **Unconfigured** — the stage has no enabled actor for the requested network. The run stops for that
  source and points you at settings. This is the expected behaviour when step 2 or 3 is missing.
- **Empty** — the stage ran and genuinely found nothing.
- **Failed** — the stage ran and errored (provider down, credentials, quota).

A source that fails does not empty the whole run: the remaining sources still contribute, and the
failure is reported rather than swallowed. The whole session only reports `failed` when it produced
no candidates at all and at least one source errored.

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
| Candidate thumbnails are often empty | Some TikTok actors report no video cover at all. The shortlist then shows no thumbnail. It deliberately does **not** substitute the author's profile picture — that would look like a video still and mislead you while judging content. |
| Instagram / YouTube niche search | Configurable but not implemented. They degrade through the unconfigured path rather than failing, and the UI says "not supported yet" rather than asking you to configure an actor. |
| Clustering | Cross-platform pattern clustering is not implemented. Pre-analysis contributes only a lexical co-occurrence signal to the score. |
| Candidate table growth | `discovered_candidates` is append-only and unbounded in this iteration. An index supports a ranged purge later. Bank rows are one per candidate per organization+network, so the bank itself does not grow with re-observations. |
| Backfilling past runs | Apify runs paid for **before** the bank existed are not imported automatically. Reading them back through `GET /v2/actor-runs` is a later step. Output captured by hand can be replayed locally — see *Seeding the bank locally from captured runs*. |
| Candidate lifecycle | The bank has no reviewed / used / discarded states yet, and no niche-relevance filtering of off-topic results. |
| Tuning constants | The Proven multiplier and the trending views-per-day proxy are defaults chosen during implementation, not product-specified values. They decide what ranks highest and are worth reviewing against real data. |

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| No **Sync actors** button | The Apify integration is not `Connected`, or the API was not restarted after deployment | Connect the integration; restart the API |
| Sync fails with a database error | The actor catalog migration has not been applied | Restart the API — migrations run at startup |
| Actor dropdown empty on the workflow step | No connector mapping for that network + area pair | Complete Part 1, step 2 |
| Route shows a raw id like `Apify · DdSkpbFy5T8FyCsor` | The actor is not in the synced catalog | Run **Sync actors**; the label falls back to the id until then |
| Log: `RESPONSE ERROR … HTTP 400 for the hashtags input shape` followed by a successful `searchQueries` attempt | Normal — the actor is a keyword scraper; the reshape retry recovered it | Nothing to do |
| Log: `TIMED OUT after ~120000 ms` | The synchronous run outlived the 120 s request timeout — most often a broad niche scraping many videos | Give the client a narrower niche (fewer derived hashtags) or request fewer results |
| Log: `CANCELLED BY THE CALLER` | The API process was shutting down mid-run. The browser can no longer cause this — discovery runs in the background, not inside the request | Check the API host/container for a restart or deployment |
| The By Search overlay says the run is *still going on the server* | The screen stopped polling after its 5-minute cap; the background run itself is unaffected and still bounded by the Apify guards | Wait, then run the discovery again — or narrow the client's niche so the run is shorter |
| Discovery ends with *"could not be completed"* | The provider errored — this is **not** a search-criterion problem | Read the terminal niche-search log line above; do not rewrite the niche |
| Log: `START` with no terminal line at all | Only possible if the API process died mid-call | Check the API host/container for a crash or restart |
| Log: 400 on **both** input shapes | The actor is not a niche-search actor at all, or a required field is missing | Read `ResponsePreview` in the log; it names the field Apify rejected. Re-map the area to an actor that serves search or hashtags |
| Log: `returned N item(s) but zero mapped` | The actor answered with a response shape the TikTok mapping does not recognise | Read `ResponsePreview` in the same log line — it carries the actual item shape — and correct `TikTokNicheSearchAdapter.MapItem` |
| Log: `found no hashtag for the search term … NOT_FOUND:<tag>` | The searched hashtag does not exist on the network. The actor and the mapping are both fine | Fix the **curated hashtags** of the client's niche in the catalog (or, for a client with no niche, give it one). Do not re-map the actor |
| Log: `N of the searched term(s) do not exist … the run continues` | Normal — one derived candidate is dead, the others resolved | Nothing to do |
| Log: `yields no searchable hashtag … no actor was called` | The client has no catalog niche and its free-text niche is blank, punctuation only, or nothing but connector words | Assign the client a catalog niche |
| Log: `UNCONFIGURED for client … empty curated hashtag list` | The client's niches exist but none of them has been curated. The run is reported as Unconfigured on purpose, never silently downgraded to a free-text search | Curate the hashtags of those catalog entries (Admin → System → Niches) |
| Log: `only the first 5 are searched … the rest are dropped` | The client is assigned to enough niches to exceed the 5-hashtag cap that keeps one run affordable | Remove niches from the client if the dropped tags matter more than the kept ones |
| **Content discovery** missing from the workflow | The seeded step row does not exist for this organization | Use **Seed catalog stages** in the workflow section |
| Discover returns the same candidates every time | Expected — they are served from the candidate bank, and the shortlist says so | Click **Refresh (billed run)** when you actually want new content |
| A second client in the same niche got results without any Apify run | Expected — the bank is shared across the organization, so the niche was only paid for once | Nothing to do |
| Log: `served N candidate(s) from the stored bank … NO provider run was dispatched` | The bank answered the search | Nothing to do — this is the run that did not cost anything |
| Log: `dispatching a BILLED provider run … Reason=…` | Either the bank was empty for this niche or you asked for a refresh; the reason is on the line | If the reason is "bank is empty" and you did not expect that, check the client's niches — a different niche selection means different hashtags and therefore a different bank key |
