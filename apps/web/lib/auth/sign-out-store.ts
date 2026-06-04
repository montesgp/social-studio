// Minimal store that tracks whether a sign-out is in progress.
// Used by app-shell to replace the "Preparing workspace" overlay with "Cerrando sesión…"
// during the brief window between clearAuthSession() and router.replace("/login").

let signingOut = false;
const listeners: Set<() => void> = new Set();

function notify(): void {
  listeners.forEach((cb) => cb());
}

export function beginSignOut(): void {
  signingOut = true;
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
