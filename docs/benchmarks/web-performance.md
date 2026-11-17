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
| JS KB | JavaScript transferred for the screen. |
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
