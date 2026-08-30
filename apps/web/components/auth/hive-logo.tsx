/**
 * The Hive mark: a hexagon cell wired to its neighbours.
 *
 * Drawn inline rather than loaded as an asset so the structure follows `currentColor` and the cell
 * follows the theme's primary — an <img> would freeze both and break on the light theme.
 *
 * The geometry is mirrored byte-for-byte in `public/images/hive-logo.svg`, which is the same mark for
 * consumers that have no React and no CSS tokens (emails, OG images, decks). Change one, change the
 * other. `app/icon.svg` is intentionally NOT a copy: it drops the wiring, because at 16px it is mud.
 */

/** Satellite nodes, evenly spaced around the cell. */
const NODES = [
  [32, 4],
  [56, 18],
  [56, 46],
  [32, 60],
  [8, 46],
  [8, 18]
] as const;

export function HiveLogo({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 64 64"
      className={className}
      fill="none"
      role="img"
      aria-label="Incoders Hive"
    >
      <g stroke="currentColor">
        <polygon points="32,7 55,20 55,44 32,57 9,44 9,20" strokeWidth={2} strokeOpacity={0.55} />
        <g strokeWidth={1.25} strokeOpacity={0.45}>
          {NODES.map(([x, y]) => (
            <line key={`link-${x}-${y}`} x1={32} y1={32} x2={x} y2={y} />
          ))}
        </g>
      </g>

      <g fill="hsl(var(--primary))">
        {NODES.map(([x, y]) => (
          <circle key={`node-${x}-${y}`} cx={x} cy={y} r={3.5} />
        ))}

        <polygon points="32,20 44,27 44,40 32,47 20,40 20,27" />
        <polygon points="32,20 44,27 32,34 20,27" fillOpacity={0.55} />
      </g>
    </svg>
  );
}
