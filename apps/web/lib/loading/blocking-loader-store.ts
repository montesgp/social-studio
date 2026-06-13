export type BlockingLoaderMode = "spinner" | "progress";

export interface BlockingLoaderOptions {
  title: string;
  description?: string;
  mode?: BlockingLoaderMode;
}

export interface BlockingLoaderSnapshot {
  isVisible: boolean;
  title: string;
  description?: string;
  mode: BlockingLoaderMode;
}

const HIDDEN_SNAPSHOT: BlockingLoaderSnapshot = {
  isVisible: false,
  title: "",
  description: undefined,
  mode: "spinner",
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
    description: current.description,
    mode: current.mode ?? "spinner",
  };
}

function publish(): void {
  const next = resolveSnapshot();
  if (
    snapshot.isVisible === next.isVisible &&
    snapshot.title === next.title &&
    snapshot.description === next.description &&
    snapshot.mode === next.mode
  ) {
    return;
  }

  snapshot = next;
  notify();
}

export function show(id: string, options: BlockingLoaderOptions): () => void {
  requests.set(id, options);
  publish();

  return () => hide(id);
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
