# Web performance benchmark

Answers one question with numbers: when a screen feels slow, is the time spent in the **backend**
(API and database) or in the **frontend** (Next.js server, JavaScript, hydration)?

Run it again whenever the web feels slow, and add a dated entry under [Results](#results).

## What it measures

`scripts/bench/web-performance/bench.mjs` drives a headless Chrome over the DevTools protocol
(no npm dependencies) and, for every screen:

| Measurement | Meaning |
|---|---|
| **Hard navigation** | Full page load of the screen (reload or typed URL). |
| **Soft navigation** | Sidebar click from `/dashboard`: what you feel when switching screens. |
| TTFB | Time until the Next.js server sends the first byte of the document. In dev mode this includes compiling the route. |
| Load | Browser `load` event: document, JavaScript and CSS downloaded and executed. |
| Ready | Time until the screen's API calls settle: the screen shows its data. |
| TBT / long tasks | Main-thread work over 50 ms per task. This is the hydration and rendering cost; while it runs, clicks do not respond. |
| JS KB | JavaScript transferred for the screen. Only reliable in dev mode; a production build preloads and caches its chunks, so it reads 0 there. Use the `First Load JS` column of `pnpm build` instead. |
| API browser vs direct | Every `/hive-api` call as the browser saw it (through the Next.js proxy) next to the same `GET` replayed directly against the API. The difference is the proxy cost; the direct value is the pure backend cost. |

Each screen is loaded `--runs` times (default 4). The **cold** column is the first run: in dev mode
it includes compiling the route. **Warm** is the median of the remaining runs.

The benchmark only replays `GET` requests, so it never changes data. It does read the screens
of a real organization, so run it against the local database.

## How to run

1. Start the stack **without ComfyUI**, so the GPU does not distort the numbers:

   ```powershell
   pwsh -File scripts/dev/Start-Studio.ps1 -SkipComfyUI
   ```

2. Put the credentials of a user who belongs to the organization you want to measure in
   `scripts/bench/web-performance/.env.local`. The file is git-ignored; never pass credentials as
   arguments.

   ```dotenv
   HIVE_BENCH_EMAIL=<user email>
   HIVE_BENCH_PASSWORD=<password>
   # Optional: organization to measure; without it the app picks its default.
   HIVE_BENCH_ORG_ID=<organization-id>
   ```

3. Measure the dev frontend (the one `Start-Studio.ps1` starts on port 3000). Restart it first if
   you want a true cold run:

   ```powershell
   node scripts/bench/web-performance/bench.mjs --label dev --web http://localhost:3000
   ```

4. Measure a production build of the same code on port 3001:

   ```powershell
   cd apps/web
   pnpm build                                   # writes .next-build
   $env:NEXT_DIST_DIR = ".next-build"; pnpm exec next start -p 3001
   # in another terminal, from the repository root:
   node scripts/bench/web-performance/bench.mjs --label prod --web http://localhost:3001
   ```

Each run prints Markdown tables and writes the raw JSON to `logs/bench/` (git-ignored).

Options: `--routes /dashboard,/clients`, `--runs 4`, `--api-replays 5`, `--quiet-ms 1500`,
`--timeout-ms 90000`, `--no-soft`, `--out <file>`, `--chrome <path>` (or `HIVE_BENCH_CHROME`).

The pure aggregation helpers are covered by `node --test scripts/bench/web-performance/stats.test.mjs`.

## How to read it

- **Direct API is fast but Ready is slow:** the backend is not the bottleneck. Look at TTFB, TBT and
  JS KB.
- **Dev cold is much slower than dev warm:** route compilation on first visit (dev mode only).
- **Dev warm is much slower than prod warm:** the cost is dev mode itself (unoptimized bundles,
  React Strict Mode double effects, the polling file watcher of `dev:fast`). Users of a deployed
  build do not pay it.
- **Prod is still slow and TBT is high:** the screen does too much JavaScript work on hydration.
  Look at the component tree of that screen.
- **Browser API is much slower than direct API:** the Next.js proxy (`/hive-api` rewrite) or the dev
  server is the bottleneck, not the API.
- **Direct API is slow:** the backend is the bottleneck; look at the slowest endpoints in the table.

## Results

Newest first. Record the machine state (what else was running) with each entry.

### 2026-11-17 - dev (`dev:fast`) vs production build

**Machine:** 6-core desktop CPU, 32 GB RAM, Windows 11. API through the AppHost `http-fast`
profile, local Docker Postgres, **ComfyUI off**. User `sysadmin@incoders.ai` on organization
`<organization-id>`. 4 runs per screen; dev measured right after a dev server restart.
Raw output: `logs/bench/web-performance-{dev,prod}.{json,md}` (local, git-ignored).

**Verdict: the backend is not the bottleneck. The slowness is the Next.js dev server.**

| Screen | Dev first visit: TTFB / ready | Dev warm: ready | Prod warm: ready | Dev sidebar click | Prod sidebar click |
|---|---|---|---|---|---|
| /dashboard | 16 211 / 16 720 | 5 992 | 222 | - | - |
| /content-generator | 2 219 / 2 497 | 1 372 | 273 | 541 | 167 |
| /single-analyzer | 6 821 / 7 162 | 1 308 | 382 | 807 | 135 |
| /ai-analysis | 2 002 / 2 231 | 1 241 | 240 | 349 | 86 |
| /clients | 2 484 / 2 739 | 1 221 | 351 | 720 | 92 |
| /publications | 2 071 / 2 311 | 999 | 218 | 53 | 27 |
| /niche-discovery | 1 672 / 1 883 | 1 077 | 231 | 409 | 67 |

All values in ms. Ready = the screen has its data.

Findings:

1. **Backend:** every API endpoint the screens call answers in **2-11 ms** directly (p95). The database
   and the API add almost nothing.
2. **Dev mode compiles each screen on its first visit.** The first `/dashboard` load waited **16 s** for
   the first byte (`Compiled /dashboard in 13.6s` in the Next.js log), `/single-analyzer` 7 s (18 s in an
   earlier run). This is the "screens take several seconds" symptom, and it repeats after every dev
   server restart.
3. **Even warm, dev is 4-5x slower than production** (full load about 1-1.4 s vs 0.2-0.4 s; sidebar
   clicks 0.3-0.8 s vs 0.03-0.2 s). Production shows no hydration long tasks (TBT 0 warm).
4. **Open lead - proxy stalls of about 4.1 s:** about 1% of the API calls that go through the Next.js
   `/hive-api` rewrite stall for **4.1-4.4 s** in both modes (dev 4 of 229, prod 2 of 240), while the
   same requests directly against the API never exceed 11 ms. The near-constant duration suggests a
   connection-level cause between the Next.js server and `http://localhost:5336` (for example
   IPv6/IPv4 fallback for `localhost` or connection reuse), not processing time. Not diagnosed yet.
   In dev this is what turns `/dashboard` warm ready into about 6 s.

Caveats: `GET /api/integrations/organization/canva` and `GET /api/social/discovery-bank` answered 400
when replayed directly (the browser calls succeeded), so they have no direct numbers. The first dev run
of this session was discarded: a script defect let the access token expire mid-run, and its direct
replays were 401s; the script now re-authenticates and excludes non-2xx replays.
