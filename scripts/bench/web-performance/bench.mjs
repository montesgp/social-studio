#!/usr/bin/env node
// Web performance benchmark for Social Studio: separates frontend cost (document, hydration, Next.js
// proxy) from backend cost (API latency) for the screens a user actually opens.
//
// It drives a headless Chrome over the DevTools protocol (no npm dependencies), logs in through the
// API, and for every screen measures:
//   - hard navigation (full page load): TTFB, DOMContentLoaded, load, long tasks (hydration) and the
//     time until the screen's API calls settle ("ready");
//   - soft navigation (sidebar click from /dashboard, what the user feels when switching screens);
//   - every /hive-api call the screen made, as seen by the browser (through the Next.js proxy) and
//     replayed directly against the API (backend only). Only GET requests are replayed.
//
// Usage and how to read the results: docs/benchmarks/web-performance.md
//
//   HIVE_BENCH_EMAIL=... HIVE_BENCH_PASSWORD=... node scripts/bench/web-performance/bench.mjs \
//     --label dev --web http://localhost:3000 --api http://localhost:5336

import { spawn } from "node:child_process";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { setTimeout as sleep } from "node:timers/promises";
import { formatMs, median, requestPhases, summarizeEndpoints, summarizeRuns, totalBlockingTime } from "./stats.mjs";

const DEFAULT_ROUTES = [
  "/dashboard",
  "/content-generator",
  "/single-analyzer",
  "/ai-analysis",
  "/clients",
  "/publications",
  "/niche-discovery"
];
const PAGE_METRICS = ["ttfbMs", "domContentLoadedMs", "loadMs", "readyMs", "longTaskCount", "longTaskMaxMs", "tbtMs", "apiCount", "apiMaxMs", "scriptKb"];
const SOFT_METRICS = ["readyMs", "longTaskCount", "longTaskMaxMs", "tbtMs", "apiCount", "apiMaxMs"];

function parseArgs(argv) {
  const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
  const args = {
    label: "run",
    web: "http://localhost:3000",
    api: "http://localhost:5336",
    routes: DEFAULT_ROUTES,
    runs: 4,
    apiReplays: 5,
    quietMs: 1500,
    timeoutMs: 90_000,
    soft: true,
    out: null,
    chrome: process.env.HIVE_BENCH_CHROME ?? null,
    repoRoot
  };
  for (let i = 2; i < argv.length; i++) {
    const flag = argv[i];
    const value = argv[i + 1];
    switch (flag) {
      case "--label": args.label = value; i++; break;
      case "--web": args.web = value.replace(/\/$/, ""); i++; break;
      case "--api": args.api = value.replace(/\/$/, ""); i++; break;
      case "--routes": args.routes = value.split(",").map((r) => r.trim()).filter(Boolean); i++; break;
      case "--runs": args.runs = Math.max(1, Number(value)); i++; break;
      case "--api-replays": args.apiReplays = Math.max(0, Number(value)); i++; break;
      case "--quiet-ms": args.quietMs = Number(value); i++; break;
      case "--timeout-ms": args.timeoutMs = Number(value); i++; break;
      case "--no-soft": args.soft = false; break;
      case "--out": args.out = value; i++; break;
      case "--chrome": args.chrome = value; i++; break;
      case "--help":
        console.log("See docs/benchmarks/web-performance.md");
        process.exit(0);
      default:
        throw new Error(`Unknown argument: ${flag}`);
    }
  }
  args.out ??= join(repoRoot, "logs", "bench", `web-performance-${args.label}-${new Date().toISOString().replace(/[:.]/g, "-")}.json`);
  return args;
}

function findChrome(explicit) {
  const candidates = [
    explicit,
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "/usr/bin/google-chrome",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
  ].filter(Boolean);
  const found = candidates.find((c) => existsSync(c));
  if (!found) throw new Error("Chrome not found. Pass --chrome <path> or set HIVE_BENCH_CHROME.");
  return found;
}

async function login(api, email, password) {
  const response = await fetch(`${api}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password })
  });
  if (!response.ok) throw new Error(`Login failed with HTTP ${response.status}. Check HIVE_BENCH_EMAIL / HIVE_BENCH_PASSWORD.`);
  return response.json();
}

/** Minimal DevTools protocol client over the WebSocket built into Node 22. */
class Cdp {
  constructor(url) {
    this.nextId = 1;
    this.pending = new Map();
    this.listeners = new Map();
    this.ws = new WebSocket(url);
    this.opened = new Promise((ok, fail) => {
      this.ws.addEventListener("open", ok, { once: true });
      this.ws.addEventListener("error", fail, { once: true });
    });
    this.ws.addEventListener("message", (event) => {
      const msg = JSON.parse(event.data);
      if (msg.id && this.pending.has(msg.id)) {
        const { ok, fail } = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        if (msg.error) fail(new Error(`${msg.error.message} (${msg.error.code})`));
        else ok(msg.result);
      } else if (msg.method) {
        for (const fn of this.listeners.get(msg.method) ?? []) fn(msg.params);
      }
    });
  }
  send(method, params = {}) {
    const id = this.nextId++;
    this.ws.send(JSON.stringify({ id, method, params }));
    return new Promise((ok, fail) => this.pending.set(id, { ok, fail }));
  }
  on(method, fn) {
    if (!this.listeners.has(method)) this.listeners.set(method, []);
    this.listeners.get(method).push(fn);
  }
  once(method) {
    return new Promise((ok) => {
      const fn = (params) => {
        this.listeners.set(method, this.listeners.get(method).filter((f) => f !== fn));
        ok(params);
      };
      this.on(method, fn);
    });
  }
  async evaluate(expression) {
    const result = await this.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(`Page evaluation failed: ${result.exceptionDetails.text}`);
    return result.result.value;
  }
  close() {
    this.ws.close();
  }
}

async function launchChrome(chromePath) {
  const profileDir = mkdtempSync(join(tmpdir(), "agent-studio-bench-chrome-"));
  const child = spawn(chromePath, [
    "--headless=new",
    "--remote-debugging-port=0",
    `--user-data-dir=${profileDir}`,
    "--no-first-run",
    "--no-default-browser-check",
    "--disable-extensions",
    "--window-size=1440,900",
    "about:blank"
  ], { stdio: "ignore" });
  const portFile = join(profileDir, "DevToolsActivePort");
  for (let i = 0; i < 100 && !existsSync(portFile); i++) await sleep(100);
  if (!existsSync(portFile)) throw new Error("Chrome did not expose a DevTools port.");
  const port = readFileSync(portFile, "utf8").split("\n")[0].trim();
  const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" })).json();
  const cdp = new Cdp(target.webSocketDebuggerUrl);
  await cdp.opened;
  const stop = async () => {
    cdp.close();
    child.kill();
    for (let i = 0; i < 20; i++) {
      try {
        rmSync(profileDir, { recursive: true, force: true });
        return;
      } catch {
        await sleep(250);
      }
    }
  };
  return { cdp, stop };
}

/** Tracks /hive-api requests in flight so a screen counts as "ready" once its API calls settle. */
class ApiTracker {
  constructor(cdp) {
    this.inflight = new Map();
    this.samples = [];
    this.scriptBytes = 0;
    this.lastActivity = performance.now();
    cdp.on("Network.requestWillBeSent", (p) => {
      if (!p.request.url.includes("/hive-api/")) return;
      const url = new URL(p.request.url);
      this.inflight.set(p.requestId, {
        method: p.request.method,
        path: url.pathname.replace(/^\/hive-api/, "") + url.search,
        headers: p.request.headers,
        timestamp: p.timestamp,
        start: performance.now()
      });
      this.lastActivity = performance.now();
    });
    cdp.on("Network.responseReceived", (p) => {
      const entry = this.inflight.get(p.requestId);
      if (!entry) return;
      entry.status = p.response.status;
      entry.phases = requestPhases(entry.timestamp, p.response.timing);
      // A server-sent event stream never finishes; it must not keep the screen "loading" forever.
      if (p.response.mimeType === "text/event-stream") this.inflight.delete(p.requestId);
    });
    const finish = (p) => {
      const entry = this.inflight.get(p.requestId);
      if (!entry) return;
      this.inflight.delete(p.requestId);
      const end = performance.now();
      this.samples.push({ ...entry, end, ms: end - entry.start, failed: p.errorText !== undefined });
      this.lastActivity = end;
    };
    cdp.on("Network.loadingFinished", finish);
    cdp.on("Network.loadingFailed", finish);
    this.resourceTypes = new Map();
    cdp.on("Network.responseReceived", (p) => this.resourceTypes.set(p.requestId, p.type));
    cdp.on("Network.loadingFinished", (p) => {
      if (this.resourceTypes.get(p.requestId) === "Script") this.scriptBytes += p.encodedDataLength;
    });
  }
  pendingPaths() {
    return [...this.inflight.values()].map((e) => `${e.method} ${e.path}`);
  }
  reset() {
    // A request cut off by a navigation may never report completion; never carry it into the next measurement.
    this.inflight.clear();
    this.samples = [];
    this.scriptBytes = 0;
    this.lastActivity = performance.now();
  }
  async waitForQuiet(quietMs, timeoutMs, extraCondition = async () => true) {
    const started = performance.now();
    while (performance.now() - started < timeoutMs) {
      const idle = this.inflight.size === 0 && performance.now() - this.lastActivity >= quietMs;
      if (idle && (await extraCondition())) return true;
      await sleep(100);
    }
    return false;
  }
}

// Installed before any page script: collects long tasks (hydration and rendering work) and LCP.
const PAGE_OBSERVER = `(() => {
  window.__hiveBench = { longTasks: [], lcp: null };
  try {
    new PerformanceObserver((list) => {
      for (const e of list.getEntries()) window.__hiveBench.longTasks.push(e.duration);
    }).observe({ type: "longtask", buffered: true });
    new PerformanceObserver((list) => {
      const entries = list.getEntries();
      window.__hiveBench.lcp = entries[entries.length - 1].startTime;
    }).observe({ type: "largest-contentful-paint", buffered: true });
  } catch {}
})();`;

function sessionScript(tokens, orgId) {
  const session = JSON.stringify({
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    accessTokenExpiresAtUtc: tokens.accessTokenExpiresAtUtc
  });
  const org = orgId ? `localStorage.setItem("social-studio.active.org", ${JSON.stringify(orgId)});` : "";
  return `(() => { try { localStorage.setItem("social-studio.auth.session", ${JSON.stringify(session)}); ${org} } catch {} })();`;
}

async function readLongTasks(cdp) {
  const tasks = (await cdp.evaluate("window.__hiveBench ? window.__hiveBench.longTasks : []")) ?? [];
  return {
    longTaskCount: tasks.length,
    longTaskMaxMs: tasks.length ? Math.max(...tasks) : 0,
    tbtMs: totalBlockingTime(tasks)
  };
}

function apiMetrics(tracker, startedAt) {
  const samples = tracker.samples.filter((s) => s.start >= startedAt);
  return {
    apiCount: samples.length,
    apiMaxMs: samples.length ? Math.max(...samples.map((s) => s.ms)) : 0,
    lastApiEnd: samples.length ? Math.max(...samples.map((s) => s.end)) : null,
    samples
  };
}

async function hardNavigation(cdp, tracker, args, route) {
  tracker.reset();
  const startedAt = performance.now();
  const loaded = cdp.once("Page.loadEventFired");
  await cdp.send("Page.navigate", { url: `${args.web}${route}` });
  await Promise.race([loaded, sleep(args.timeoutMs)]);
  const settled = await tracker.waitForQuiet(args.quietMs, args.timeoutMs);
  const nav = await cdp.evaluate(`(() => {
    const n = performance.getEntriesByType("navigation")[0];
    return n ? { ttfb: n.responseStart, dcl: n.domContentLoadedEventEnd, load: n.loadEventEnd, path: location.pathname } : null;
  })()`);
  if (nav?.path === "/login" && route !== "/login") {
    throw new Error(`${route} redirected to /login: the session was rejected or expired. Re-run with fewer --runs or routes.`);
  }
  const api = apiMetrics(tracker, startedAt);
  const loadMs = nav?.load ?? null;
  const readyMs = Math.max(loadMs ?? 0, api.lastApiEnd !== null ? api.lastApiEnd - startedAt : 0);
  return {
    finalPath: nav?.path ?? null,
    settled,
    pending: settled ? [] : tracker.pendingPaths(),
    ttfbMs: nav?.ttfb ?? null,
    domContentLoadedMs: nav?.dcl ?? null,
    loadMs,
    readyMs,
    scriptKb: tracker.scriptBytes / 1024,
    ...(await readLongTasks(cdp)),
    apiCount: api.apiCount,
    apiMaxMs: api.apiMaxMs,
    apiSamples: api.samples.map(({ method, path, status, ms, failed, phases }) => ({ method, path, status, ms, failed, phases }))
  };
}

async function softNavigation(cdp, tracker, args, route) {
  const hasLink = await cdp.evaluate(`!!document.querySelector('a[href="${route}"]')`);
  if (!hasLink) return { skipped: "no sidebar link on /dashboard" };
  await cdp.evaluate("window.__hiveBench && (window.__hiveBench.longTasks = [])");
  tracker.reset();
  const startedAt = performance.now();
  await cdp.evaluate(`document.querySelector('a[href="${route}"]').click()`);
  const target = route;
  const settled = await tracker.waitForQuiet(args.quietMs, args.timeoutMs, async () => {
    const path = await cdp.evaluate("location.pathname");
    return path !== "/dashboard" || target === "/dashboard";
  });
  const api = apiMetrics(tracker, startedAt);
  const navigatedAt = performance.now();
  const readyMs = api.lastApiEnd !== null ? api.lastApiEnd - startedAt : navigatedAt - startedAt - args.quietMs;
  return {
    finalPath: await cdp.evaluate("location.pathname"),
    settled,
    pending: settled ? [] : tracker.pendingPaths(),
    readyMs,
    ...(await readLongTasks(cdp)),
    apiCount: api.apiCount,
    apiMaxMs: api.apiMaxMs,
    apiSamples: api.samples.map(({ method, path, status, ms, failed, phases }) => ({ method, path, status, ms, failed, phases }))
  };
}

/**
 * Replays every GET the screens made directly against the API: pure backend latency. Uses a fresh
 * token, since the one captured from the browser may have expired during a long run.
 */
async function replayApi(args, requests, accessToken) {
  const unique = new Map();
  for (const r of requests) if (r.method === "GET" && !unique.has(r.path)) unique.set(r.path, r.headers);
  const samples = [];
  for (const [path, headers] of unique) {
    const replayHeaders = {};
    for (const [k, v] of Object.entries(headers)) {
      if (/^(x-tenant-id|accept)$/i.test(k)) replayHeaders[k] = v;
    }
    replayHeaders.Authorization = `Bearer ${accessToken}`;
    for (let i = 0; i < args.apiReplays; i++) {
      const started = performance.now();
      const response = await fetch(`${args.api}${path}`, { headers: replayHeaders });
      await response.arrayBuffer();
      samples.push({ method: "GET", path, status: response.status, ms: performance.now() - started });
    }
  }
  return samples;
}

function printReport(report) {
  const lines = [];
  lines.push(`\n## ${report.label} (${report.web}) - ${report.startedAtUtc}`);
  lines.push("\n### Hard navigation (full load), ms - cold = first run, warm = median of the rest\n");
  lines.push("| Screen | TTFB cold/warm | Load cold/warm | Ready cold/warm | TBT cold/warm | Long tasks warm | API calls | Slowest API warm | JS KB |");
  lines.push("|---|---|---|---|---|---|---|---|---|");
  for (const s of report.screens) {
    const { cold, warm } = s.hardSummary;
    lines.push(`| ${s.route}${s.redirectedTo ? ` (-> ${s.redirectedTo})` : ""} | ${formatMs(cold.ttfbMs)} / ${formatMs(warm.ttfbMs)} | ${formatMs(cold.loadMs)} / ${formatMs(warm.loadMs)} | ${formatMs(cold.readyMs)} / ${formatMs(warm.readyMs)} | ${formatMs(cold.tbtMs)} / ${formatMs(warm.tbtMs)} | ${formatMs(warm.longTaskCount)} | ${formatMs(warm.apiCount)} | ${formatMs(warm.apiMaxMs)} | ${formatMs(warm.scriptKb)} |`);
  }
  if (report.screens.some((s) => s.softSummary)) {
    lines.push("\n### Soft navigation (sidebar click from /dashboard), ms\n");
    lines.push("| Screen | Ready cold/warm | TBT cold/warm | API calls | Slowest API warm |");
    lines.push("|---|---|---|---|---|");
    for (const s of report.screens.filter((x) => x.softSummary)) {
      const { cold, warm } = s.softSummary;
      lines.push(`| ${s.route} | ${formatMs(cold.readyMs)} / ${formatMs(warm.readyMs)} | ${formatMs(cold.tbtMs)} / ${formatMs(warm.tbtMs)} | ${formatMs(warm.apiCount)} | ${formatMs(warm.apiMaxMs)} |`);
    }
  }
  lines.push("\n### API: through the Next.js proxy (browser) vs direct (backend only), ms\n");
  lines.push("| Endpoint | Browser p50 | Browser max | Direct p50 | Direct p95 | Direct max |");
  lines.push("|---|---|---|---|---|---|");
  const direct = new Map(report.apiDirect.map((e) => [e.endpoint, e]));
  for (const b of report.apiBrowser) {
    const d = direct.get(b.endpoint);
    lines.push(`| \`${b.endpoint}\` | ${formatMs(b.p50)} | ${formatMs(b.max)} | ${formatMs(d?.p50)} | ${formatMs(d?.p95)} | ${formatMs(d?.max)} |`);
  }
  if (report.slowApiCalls.length > 0) {
    lines.push("\n### Browser API calls over 1 s, with phases (ms)\n");
    lines.push("| Screen | Endpoint | Total | Queued in browser | DNS | Connect | Server wait | Reused connection |");
    lines.push("|---|---|---|---|---|---|---|---|");
    for (const s of report.slowApiCalls) {
      const p = s.phases ?? {};
      lines.push(`| ${s.screen} (${s.kind}) | \`${s.method} ${s.path}\` | ${formatMs(s.ms)} | ${formatMs(p.queuedMs)} | ${formatMs(p.dnsMs)} | ${formatMs(p.connectMs)} | ${formatMs(p.waitMs)} | ${p.reusedConnection === undefined ? "-" : p.reusedConnection} |`);
    }
  }
  console.log(lines.join("\n"));
}

/** Loads KEY=VALUE pairs from the git-ignored .env.local next to this script, without overriding the environment. */
function loadLocalEnv() {
  const file = join(dirname(fileURLToPath(import.meta.url)), ".env.local");
  if (!existsSync(file)) return;
  for (const line of readFileSync(file, "utf8").split(/\r?\n/)) {
    const match = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/);
    if (match && process.env[match[1]] === undefined) process.env[match[1]] = match[2].replace(/^"(.*)"$/, "$1");
  }
}

async function main() {
  const args = parseArgs(process.argv);
  loadLocalEnv();
  const email = process.env.HIVE_BENCH_EMAIL;
  const password = process.env.HIVE_BENCH_PASSWORD;
  if (!email || !password) throw new Error("Set HIVE_BENCH_EMAIL and HIVE_BENCH_PASSWORD in the environment or in scripts/bench/web-performance/.env.local (never pass credentials as arguments).");

  const tokens = await login(args.api, email, password);
  const { cdp, stop } = await launchChrome(findChrome(args.chrome));
  try {
    await cdp.send("Page.enable");
    await cdp.send("Network.enable");
    await cdp.send("Network.setCacheDisabled", { cacheDisabled: false });
    await cdp.send("Page.addScriptToEvaluateOnNewDocument", { source: PAGE_OBSERVER });
    await cdp.send("Page.addScriptToEvaluateOnNewDocument", { source: sessionScript(tokens, process.env.HIVE_BENCH_ORG_ID) });
    const expires = Math.floor(Date.parse(tokens.accessTokenExpiresAtUtc) / 1000);
    for (const [name, value] of [
      ["social_studio_access_token", tokens.accessToken],
      ["social_studio_refresh_token", tokens.refreshToken],
      ["social_studio_access_token_expires_at", tokens.accessTokenExpiresAtUtc]
    ]) {
      await cdp.send("Network.setCookie", { name, value: encodeURIComponent(value), url: args.web, path: "/", sameSite: "Lax", expires });
    }

    const tracker = new ApiTracker(cdp);
    const report = { label: args.label, web: args.web, api: args.api, startedAtUtc: new Date().toISOString(), runs: args.runs, screens: [] };
    const allBrowserSamples = [];

    for (const route of args.routes) {
      const hardRuns = [];
      for (let i = 0; i < args.runs; i++) {
        const run = await hardNavigation(cdp, tracker, args, route);
        hardRuns.push(run);
        allBrowserSamples.push(...tracker.samples.map((s) => ({ ...s })));
        process.stderr.write(`[${args.label}] hard ${route} #${i + 1}: ready ${formatMs(run.readyMs)} ms, ttfb ${formatMs(run.ttfbMs)} ms, ${run.apiCount} API calls${run.settled ? "" : ` (did not settle; pending: ${run.pending.join(", ") || "none, kept busy by new calls"})`}\n`);
      }
      const screen = {
        route,
        redirectedTo: hardRuns[0].finalPath && hardRuns[0].finalPath !== route ? hardRuns[0].finalPath : null,
        hardRuns,
        hardSummary: summarizeRuns(hardRuns, PAGE_METRICS)
      };
      report.screens.push(screen);
    }

    if (args.soft) {
      for (const screen of report.screens) {
        if (screen.route === "/dashboard") continue;
        const softRuns = [];
        for (let i = 0; i < args.runs; i++) {
          await hardNavigation(cdp, tracker, args, "/dashboard");
          const run = await softNavigation(cdp, tracker, args, screen.route);
          if (run.skipped) {
            screen.softSkipped = run.skipped;
            break;
          }
          softRuns.push(run);
          allBrowserSamples.push(...tracker.samples.map((s) => ({ ...s })));
          process.stderr.write(`[${args.label}] soft ${screen.route} #${i + 1}: ready ${formatMs(run.readyMs)} ms\n`);
        }
        if (softRuns.length) {
          screen.softRuns = softRuns;
          screen.softSummary = summarizeRuns(softRuns, SOFT_METRICS);
        }
      }
    }

    const replayTokens = await login(args.api, email, password);
    const directSamples = await replayApi(args, allBrowserSamples, replayTokens.accessToken);
    const directOk = directSamples.filter((s) => s.status >= 200 && s.status < 300);
    report.apiBrowser = summarizeEndpoints(allBrowserSamples.map(({ method, path, ms }) => ({ method, path, ms })));
    // Only successful replays count: a rejected request answers in a few ms and would fake a fast backend.
    report.apiDirect = summarizeEndpoints(directOk);
    report.apiDirectNon2xx = directSamples.filter((s) => s.status < 200 || s.status >= 300).map(({ path, status }) => ({ path, status }));
    report.medianDirectApiMs = median(directOk.map((s) => s.ms));
    report.slowApiCalls = report.screens.flatMap((s) => [
      ...s.hardRuns.flatMap((r) => r.apiSamples.map((a) => ({ screen: s.route, kind: "hard", ...a }))),
      ...(s.softRuns ?? []).flatMap((r) => r.apiSamples.map((a) => ({ screen: s.route, kind: "soft", ...a })))
    ]).filter((a) => a.ms > 1000);
    if (report.apiDirectNon2xx.length > 0) {
      process.stderr.write(`[${args.label}] ${report.apiDirectNon2xx.length} direct replays were not 2xx and are excluded; see apiDirectNon2xx in the JSON.\n`);
    }

    mkdirSync(dirname(args.out), { recursive: true });
    writeFileSync(args.out, JSON.stringify(report, null, 2));
    printReport(report);
    console.log(`\nRaw results: ${args.out}`);
  } finally {
    await stop();
  }
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
