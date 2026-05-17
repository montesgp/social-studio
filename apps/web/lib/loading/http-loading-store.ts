let pendingCount = 0;
const listeners: Set<() => void> = new Set();

function notify(): void {
  listeners.forEach((cb) => cb());
}

export function increment(): void {
  pendingCount += 1;
  notify();
}

export function decrement(): void {
  pendingCount = Math.max(0, pendingCount - 1);
  notify();
}

export function subscribe(cb: () => void): () => void {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

export function getSnapshot(): boolean {
  return pendingCount > 0;
}
