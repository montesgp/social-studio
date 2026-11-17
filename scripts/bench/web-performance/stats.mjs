// Pure aggregation helpers for the web performance benchmark (no I/O, unit-tested in stats.test.mjs).

/** Nearest-rank percentile of a list of numbers; `null` for an empty list. */
export function percentile(values, p) {
  const sorted = values.filter((v) => Number.isFinite(v)).sort((a, b) => a - b);
  if (sorted.length === 0) return null;
  if (p <= 0) return sorted[0];
  if (p >= 100) return sorted[sorted.length - 1];
  const rank = Math.ceil((p / 100) * sorted.length);
  return sorted[Math.max(0, rank - 1)];
}

export function median(values) {
  return percentile(values, 50);
}

/** Total blocking time: the part of every long task above the 50 ms budget. */
export function totalBlockingTime(longTaskDurations) {
  return longTaskDurations.reduce((sum, d) => sum + Math.max(0, d - 50), 0);
}

/**
 * Splits repeated measurements of one screen into the first ("cold") run and the median of the
 * remaining ("warm") runs, per numeric metric. With a single run, warm is `null`.
 */
export function summarizeRuns(runs, metricKeys) {
  const cold = {};
  const warm = {};
  for (const key of metricKeys) {
    cold[key] = runs.length > 0 ? runs[0][key] ?? null : null;
    warm[key] = runs.length > 1 ? median(runs.slice(1).map((r) => r[key])) : null;
  }
  return { cold, warm };
}

/** Groups API samples by "METHOD path" and reports count, p50, p95 and max duration in ms. */
export function summarizeEndpoints(samples) {
  const groups = new Map();
  for (const s of samples) {
    const key = `${s.method} ${s.path}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(s.ms);
  }
  return [...groups.entries()]
    .map(([endpoint, ms]) => ({
      endpoint,
      count: ms.length,
      p50: percentile(ms, 50),
      p95: percentile(ms, 95),
      max: percentile(ms, 100)
    }))
    .sort((a, b) => (b.p50 ?? 0) - (a.p50 ?? 0));
}

/**
 * Splits one request into phases from the DevTools protocol: `willBeSentTimestamp` is
 * Network.requestWillBeSent.timestamp (seconds) and `timing` is Network.Response.timing, whose offsets
 * are ms relative to `timing.requestTime` (seconds) and -1 when a phase did not happen.
 * queuedMs = time in the browser before any network activity (connection limit, scheduling);
 * waitMs = server time to first byte after the request was sent.
 */
export function requestPhases(willBeSentTimestamp, timing) {
  if (!timing) return null;
  const firstNetworkOffset = [timing.dnsStart, timing.connectStart, timing.sendStart].find((v) => v >= 0) ?? 0;
  const span = (start, end) => (start >= 0 && end >= 0 ? end - start : 0);
  const round = (v) => Math.round(v * 1000) / 1000;
  return {
    queuedMs: round((timing.requestTime - willBeSentTimestamp) * 1000 + firstNetworkOffset),
    dnsMs: round(span(timing.dnsStart, timing.dnsEnd)),
    connectMs: round(span(timing.connectStart, timing.connectEnd)),
    waitMs: round(span(timing.sendEnd, timing.receiveHeadersEnd)),
    reusedConnection: timing.connectStart < 0
  };
}

export function formatMs(value) {
  return value === null || value === undefined ? "-" : `${Math.round(value)}`;
}
