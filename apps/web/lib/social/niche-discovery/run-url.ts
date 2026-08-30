/**
 * URL round-tripping for the niche-discovery route (design §10 — no cross-run listing endpoint).
 * The run id in the URL is the operator's only handle to a run that already cost real API quota;
 * losing it on reload means losing access to that run.
 */

const ROUTE = "/niche-discovery";
const RUN_PARAM = "run";

export type RunSearchParams = URLSearchParams | Record<string, string | string[] | undefined> | null | undefined;

export function buildRunPath(runId: string | null): string {
  if (!runId) {
    return ROUTE;
  }
  return `${ROUTE}?${RUN_PARAM}=${encodeURIComponent(runId)}`;
}

export function readRunIdFromSearchParams(searchParams: RunSearchParams): string | null {
  if (!searchParams) {
    return null;
  }

  const raw = searchParams instanceof URLSearchParams ? searchParams.get(RUN_PARAM) : searchParams[RUN_PARAM];

  const value = Array.isArray(raw) ? raw[0] : raw;
  if (!value) {
    return null;
  }

  return value;
}
