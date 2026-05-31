/**
 * usePackageWorkspace — INC-6
 *
 * Owns the multi-client package tree state (`clientPackages`), hydrated from
 * GET /api/social/single-analyzer/{analysisId}/packages and surviving refresh.
 *
 * Also exposes per-client helpers:
 *   hasStoredFormatsForClient(clientId) — true when the client has ≥1 stored format in the tree
 *   markFormatDeleted(clientId, formatKey) — removes format from the tree (after delete)
 *   markFormatRegenerated(clientId, formatKey, assets) — updates assets in the tree (after regen)
 *   refreshPackages() — re-fetches the tree from the backend
 */

"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { listAnalysisPackages } from "@/lib/api/content-generation";
import type { ClientPackageEntry, PackageAssetEntry } from "@/lib/api/types";

export type ClientPackages = Record<string, ClientPackageEntry>;

export interface UsePackageWorkspaceResult {
  /** Map of clientId → ClientPackageEntry (formats + assets). */
  clientPackages: ClientPackages;
  /** True while the initial fetch or a refresh is in flight. */
  isLoadingPackages: boolean;
  /** Returns true when the given client has at least one stored format in the tree. */
  hasStoredFormatsForClient: (clientId: string) => boolean;
  /** Removes a format from the in-memory tree (called after a successful delete). */
  markFormatDeleted: (clientId: string, formatKey: string) => void;
  /** Updates a format's assets in the in-memory tree (called after a successful regeneration). */
  markFormatRegenerated: (clientId: string, formatKey: string, assets: PackageAssetEntry[]) => void;
  /** Appends a client entry or updates it (called after a new job completes for a client). */
  refreshPackages: () => void;
}

export function usePackageWorkspace(
  analysisId: string | null | undefined,
  token: string | null | undefined
): UsePackageWorkspaceResult {
  const [clientPackages, setClientPackages] = useState<ClientPackages>({});
  const [isLoadingPackages, setIsLoadingPackages] = useState(false);
  // Ref to track the last analysisId that was fetched — avoids re-fetching on token refresh.
  const lastFetchedAnalysisIdRef = useRef<string | null>(null);
  const refreshCounterRef = useRef(0);
  const [refreshTick, setRefreshTick] = useState(0);

  useEffect(() => {
    if (!analysisId || !token) return;

    const abortController = new AbortController();
    setIsLoadingPackages(true);

    void listAnalysisPackages(analysisId, { token, signal: abortController.signal })
      .then((response) => {
        lastFetchedAnalysisIdRef.current = analysisId;
        const map: ClientPackages = {};
        for (const entry of response.clients) {
          map[entry.clientId] = entry;
        }
        setClientPackages(map);
      })
      .catch((err) => {
        // Ignore abort errors (component unmounted or analysisId changed)
        if (err instanceof Error && err.name === "AbortError") return;
        // For other errors (e.g. 404 = no packages yet), reset to empty
        setClientPackages({});
      })
      .finally(() => {
        setIsLoadingPackages(false);
      });

    return () => {
      abortController.abort();
    };
  }, [analysisId, token, refreshTick]);

  const hasStoredFormatsForClient = useCallback(
    (clientId: string): boolean => {
      const entry = clientPackages[clientId];
      return Boolean(entry && entry.formats.length > 0);
    },
    [clientPackages]
  );

  const markFormatDeleted = useCallback(
    (clientId: string, formatKey: string) => {
      setClientPackages((prev) => {
        const entry = prev[clientId];
        if (!entry) return prev;

        const updatedFormats = entry.formats.filter(
          (f) => f.formatKey !== formatKey
        );

        if (updatedFormats.length === 0) {
          // No formats left — remove the client entry entirely
          const next = { ...prev };
          delete next[clientId];
          return next;
        }

        return {
          ...prev,
          [clientId]: { ...entry, formats: updatedFormats },
        };
      });
    },
    []
  );

  const markFormatRegenerated = useCallback(
    (clientId: string, formatKey: string, assets: PackageAssetEntry[]) => {
      setClientPackages((prev) => {
        const entry = prev[clientId];
        if (!entry) return prev;

        const updatedFormats = entry.formats.map((f) => {
          if (f.formatKey !== formatKey) return f;
          return { ...f, assets };
        });

        return {
          ...prev,
          [clientId]: { ...entry, formats: updatedFormats },
        };
      });
    },
    []
  );

  const refreshPackages = useCallback(() => {
    refreshCounterRef.current += 1;
    setRefreshTick(refreshCounterRef.current);
  }, []);

  return {
    clientPackages,
    isLoadingPackages,
    hasStoredFormatsForClient,
    markFormatDeleted,
    markFormatRegenerated,
    refreshPackages,
  };
}
