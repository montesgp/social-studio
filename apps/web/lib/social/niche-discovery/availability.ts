/**
 * The zero-fill trap's last line of defence (ND-05, design §11).
 *
 * A metric the source network does not report MUST render as "unavailable", never as `0` —
 * conflating the two would let a real zero and a missing measurement rank the same. Every
 * numeric field surfaced by the dashboard/detail view MUST pass through this module rather than
 * being interpolated directly.
 */

export function isAvailable(value: number | null | undefined): value is number {
  return value !== null && value !== undefined;
}

export function formatAvailableNumber(value: number | null | undefined): string {
  if (!isAvailable(value)) {
    return "unavailable";
  }

  return value.toLocaleString("en-US");
}
