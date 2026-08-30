/**
 * Pure predicate behind the Content Studio per-slide degrade badge (CAFF-CS-01/02, design D6).
 *
 * The backend writes `CompositionSpecEnvelope.DegradeReason` only on the two deterministic
 * degrade branches (CAFF-CG-04); every other slide's envelope omits the key entirely
 * (`JsonIgnore(Condition = WhenWritingNull)`). `Envelope`'s `[key: string]: unknown` index
 * signature means the value survives `JSON.parse`/`JSON.stringify` untouched, so this function
 * is the only place that needs to know the shape and the "truthy non-empty string" contract.
 */
export interface DegradableEnvelope {
  degradeReason?: string | null;
  [key: string]: unknown;
}

/** Returns the degrade token when present and non-empty, otherwise `null` (no badge). */
export function getDegradeReason(envelope: DegradableEnvelope | null | undefined): string | null {
  const reason = envelope?.degradeReason;
  return typeof reason === "string" && reason.length > 0 ? reason : null;
}
