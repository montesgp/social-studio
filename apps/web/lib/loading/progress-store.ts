export interface ProgressState {
  isVisible: boolean;
  value: number;
  label: string;
  sublabel?: string;
}

let state: ProgressState = {
  isVisible: false,
  value: 0,
  label: "",
  sublabel: undefined,
};

let hideTimeout: ReturnType<typeof setTimeout> | null = null;
const listeners: Set<() => void> = new Set();

function notify(): void {
  listeners.forEach((cb) => cb());
}

function setState(next: Partial<ProgressState>): void {
  state = { ...state, ...next };
  notify();
}

export function start(label: string): void {
  if (hideTimeout !== null) {
    clearTimeout(hideTimeout);
    hideTimeout = null;
  }
  setState({ isVisible: true, value: 0, label, sublabel: undefined });
}

export function update(value: number, label?: string, sublabel?: string): void {
  setState({
    value: Math.min(100, Math.max(0, value)),
    ...(label !== undefined ? { label } : {}),
    sublabel,
  });
}

export function complete(): void {
  setState({ value: 100 });
  hideTimeout = setTimeout(() => {
    setState({ isVisible: false });
    hideTimeout = null;
  }, 600);
}

export function reset(): void {
  if (hideTimeout !== null) {
    clearTimeout(hideTimeout);
    hideTimeout = null;
  }
  setState({ isVisible: false, value: 0, sublabel: undefined });
}

export function subscribe(cb: () => void): () => void {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

export function getSnapshot(): ProgressState {
  return state;
}
