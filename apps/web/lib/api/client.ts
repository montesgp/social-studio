import { ApiError, ProblemDetails } from "@/lib/api/types";
import * as httpLoadingStore from "@/lib/loading/http-loading-store";

const BROWSER_API_BASE_URL = "/hive-api";

function getServerApiBaseUrl() {
  const configuredBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL;
  if (!configuredBaseUrl) {
    throw new Error("NEXT_PUBLIC_API_BASE_URL must be configured for server-side API requests.");
  }

  return configuredBaseUrl.endsWith("/") ? configuredBaseUrl.slice(0, -1) : configuredBaseUrl;
}

export async function apiRequest<T>(path: string, init?: RequestInit, token?: string): Promise<T> {
  const headers = new Headers(init?.headers);
  if (init?.body !== undefined && init.body !== null && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  const baseUrl = typeof window === "undefined" ? getServerApiBaseUrl() : BROWSER_API_BASE_URL;
  const isBrowser = typeof window !== "undefined";

  if (isBrowser) {
    httpLoadingStore.increment();
  }

  let response: Response;
  try {
    response = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers,
      cache: "no-store"
    });
  } finally {
    if (isBrowser) {
      httpLoadingStore.decrement();
    }
  }

  if (!response.ok) {
    let problem: ProblemDetails | undefined;
    try {
      problem = (await response.json()) as ProblemDetails;
    } catch {
      // ignored
    }

    const error: ApiError = {
      message: problem?.detail ?? problem?.title ?? `HTTP ${response.status}`,
      problem
    };

    throw error;
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}
