// Minimal store that tracks whether a sign-out is in progress.
// Used by app-shell to replace the "Preparing workspace" overlay with "Cerrando sesión…"
// during the brief window between clearAuthSession() and router.replace("/login").

let signingOut = false;
let resetTimeout: ReturnType<typeof setTimeout> | null = null;
const listeners: Set<() => void> = new Set();

function notify(): void {
  listeners.forEach((cb) => cb());
}

export function beginSignOut(): void {
  if (resetTimeout) {
    clearTimeout(resetTimeout);
  }

  signingOut = true;
  notify();

  resetTimeout = setTimeout(() => {
    resetSignOut();
  }, 10_000);
}

export function resetSignOut(): void {
  if (resetTimeout) {
    clearTimeout(resetTimeout);
    resetTimeout = null;
  }

  if (!signingOut) {
    return;
  }

  signingOut = false;
  notify();
}

export function subscribe(cb: () => void): () => void {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

export function getSnapshot(): boolean {
  return signingOut;
}
