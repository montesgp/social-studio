/**
 * Humanizes a content format key for display: `reel-script` → `Reel Script`.
 *
 * Format keys are wire identifiers (`feed-carousel`, `reel-script`, `feed-single`). Rendering the
 * raw slug leaked an internal identifier into the operator's view and read as unfinished UI.
 */
export function formatLabel(formatKey: string): string {
  if (!formatKey) {
    return "";
  }

  return formatKey
    .split(/[-_\s]+/)
    .filter((word) => word.length > 0)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(" ");
}
