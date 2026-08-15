/**
 * ND-13 — derives a navigable URL for one evidence row. `NicheDiscoveryEvidenceRecord` (design §9)
 * stores no URL column, only the provider-agnostic `sourceKey` + `externalRef` pair, so the URL is
 * rebuilt here per known source rather than trusting a raw provider field. An unrecognised source
 * key returns `null` — never a fabricated link.
 */

export type EvidenceKind = "Channel" | "Video";

export function buildEvidenceUrl(kind: EvidenceKind, sourceKey: string, externalRef: string): string | null {
  if (!externalRef.trim()) {
    return null;
  }

  if (sourceKey === "youtube-data-api") {
    return kind === "Channel"
      ? `https://www.youtube.com/channel/${externalRef}`
      : `https://www.youtube.com/watch?v=${externalRef}`;
  }

  return null;
}
