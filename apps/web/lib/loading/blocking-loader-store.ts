// Shared pub/sub store backing the app-wide full-screen stage overlay (StageOverlay).
// Any tool can declare its own ordered list of REAL stages, move the active one forward as work
// progresses, and mark a terminal success/failure — without ever inventing a completion percentage.
//
// Multiple callers may request the overlay concurrently (e.g. a background run started from one
// screen while the shell is still resolving initial data); like the previous title/description
// store, the most recently registered request wins the visible snapshot.

export interface BlockingLoaderStage {
  key: string;
  label: string;
}

export type BlockingLoaderOutcome = "running" | "done" | "failed";

export interface BlockingLoaderOptions {
  title: string;
  /** Ordered, real stages. A tool with nothing granular to report may pass a single stage. */
  stages: readonly BlockingLoaderStage[];
  /** Key of the currently active stage. Defaults to the first stage when omitted. */
  activeKey?: string | null;
  outcome?: BlockingLoaderOutcome;
  /** Error detail shown when outcome is "failed". */
  failureDetail?: string | null;
}

export interface BlockingLoaderSnapshot {
  isVisible: boolean;
  title: string;
  stages: readonly BlockingLoaderStage[];
  activeKey: string | null;
  outcome: BlockingLoaderOutcome;
  failureDetail: string | null;
}

const HIDDEN_SNAPSHOT: BlockingLoaderSnapshot = {
  isVisible: false,
  title: "",
  stages: [],
  activeKey: null,
  outcome: "running",
  failureDetail: null,
};

const requests = new Map<string, BlockingLoaderOptions>();
const listeners: Set<() => void> = new Set();

let snapshot: BlockingLoaderSnapshot = HIDDEN_SNAPSHOT;

function notify(): void {
  listeners.forEach((cb) => cb());
}

function resolveSnapshot(): BlockingLoaderSnapshot {
  let current: BlockingLoaderOptions | undefined;

  for (const request of requests.values()) {
    current = request;
  }

  if (!current) {
    return HIDDEN_SNAPSHOT;
  }

  return {
    isVisible: true,
    title: current.title,
    stages: current.stages,
    activeKey: current.activeKey ?? current.stages[0]?.key ?? null,
    outcome: current.outcome ?? "running",
    failureDetail: current.failureDetail ?? null,
  };
}

function publish(): void {
  const next = resolveSnapshot();
  if (
    snapshot.isVisible === next.isVisible &&
    snapshot.title === next.title &&
    snapshot.stages === next.stages &&
    snapshot.activeKey === next.activeKey &&
    snapshot.outcome === next.outcome &&
    snapshot.failureDetail === next.failureDetail
  ) {
    return;
  }

  snapshot = next;
  notify();
}

/** Registers/replaces a full request (stages included) for `id` and shows the overlay. */
export function show(id: string, options: BlockingLoaderOptions): () => void {
  requests.set(id, options);
  publish();

  return () => hide(id);
}

/** Moves `id`'s active stage forward without rebuilding its stage list. */
export function setActiveStage(id: string, activeKey: string): void {
  const current = requests.get(id);
  if (!current) return;

  requests.set(id, { ...current, activeKey, outcome: "running" });
  publish();
}

/** Marks `id`'s request as terminally succeeded or failed. */
export function setOutcome(id: string, outcome: BlockingLoaderOutcome, failureDetail?: string | null): void {
  const current = requests.get(id);
  if (!current) return;

  requests.set(id, { ...current, outcome, failureDetail: failureDetail ?? null });
  publish();
}

export function hide(id: string): void {
  if (!requests.delete(id)) {
    return;
  }

  publish();
}

export function subscribe(cb: () => void): () => void {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

export function getSnapshot(): BlockingLoaderSnapshot {
  return snapshot;
}

export function getServerSnapshot(): BlockingLoaderSnapshot {
  return HIDDEN_SNAPSHOT;
}
