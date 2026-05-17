import * as progressStore from "@/lib/loading/progress-store";

export interface UseProgressReturn {
  start: (label: string) => void;
  update: (value: number, label?: string, sublabel?: string) => void;
  complete: () => void;
  reset: () => void;
}

export function useProgress(): UseProgressReturn {
  return {
    start: (label: string) => progressStore.start(label),
    update: (value: number, label?: string, sublabel?: string) =>
      progressStore.update(value, label, sublabel),
    complete: () => progressStore.complete(),
    reset: () => progressStore.reset(),
  };
}
