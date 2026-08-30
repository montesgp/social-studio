/**
 * The honeycomb field behind the sign-in screen.
 *
 * Generated as inline SVG instead of a bitmap: it scales to any viewport without a 2 MB hero image,
 * costs one paint, and re-colours itself from the theme tokens. Every value is derived from the
 * cell's coordinates — never from Math.random — so the server and the client render identical
 * markup and hydration stays quiet.
 */

const CELL_WIDTH = 96;
const CELL_HEIGHT = 83;
const COLUMN_STEP = CELL_WIDTH * 0.75; // Flat-top hexagons interlock at three quarters of a width.
const ROW_STEP = CELL_HEIGHT;
const COLUMNS = 20;
const ROWS = 11;

const VIEW_WIDTH = COLUMNS * COLUMN_STEP;
const VIEW_HEIGHT = ROWS * ROW_STEP;

/** Deterministic 0..1 noise for a cell, so the comb looks scattered but renders identically twice. */
function cellNoise(column: number, row: number): number {
  const seeded = Math.sin(column * 12.9898 + row * 78.233) * 43758.5453;
  return seeded - Math.floor(seeded);
}

function hexPoints(originX: number, originY: number): string {
  const quarter = CELL_WIDTH / 4;
  const half = CELL_HEIGHT / 2;
  return [
    [originX + quarter, originY],
    [originX + quarter * 3, originY],
    [originX + CELL_WIDTH, originY + half],
    [originX + quarter * 3, originY + CELL_HEIGHT],
    [originX + quarter, originY + CELL_HEIGHT],
    [originX, originY + half]
  ]
    .map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`)
    .join(" ");
}

interface Cell {
  key: string;
  points: string;
  fillOpacity: number;
  strokeOpacity: number;
  accent: boolean;
  pulseDelay: number;
}

function buildCells(): Cell[] {
  const cells: Cell[] = [];

  for (let column = 0; column < COLUMNS; column += 1) {
    for (let row = 0; row < ROWS; row += 1) {
      const noise = cellNoise(column, row);
      const originX = column * COLUMN_STEP;
      const originY = row * ROW_STEP + (column % 2 === 0 ? 0 : CELL_HEIGHT / 2) - CELL_HEIGHT / 2;

      // Cells fade out towards the top of the field, which keeps the card the brightest thing here.
      const depth = row / (ROWS - 1);

      cells.push({
        key: `${column}-${row}`,
        points: hexPoints(originX, originY),
        fillOpacity: 0.02 + noise * 0.06 * (0.35 + depth),
        strokeOpacity: 0.05 + noise * 0.14 * (0.3 + depth),
        accent: noise > 0.93,
        pulseDelay: Math.round(noise * 9000)
      });
    }
  }

  return cells;
}

const CELLS = buildCells();

export function HiveBackdrop({ className }: { className?: string }) {
  return (
    <div className={className} aria-hidden="true">
      <svg
        viewBox={`0 0 ${VIEW_WIDTH} ${VIEW_HEIGHT}`}
        preserveAspectRatio="xMidYMid slice"
        className="h-full w-full"
      >
        <defs>
          <linearGradient id="hive-cell" x1="0" y1="0" x2="0.4" y2="1">
            <stop offset="0%" stopColor="hsl(var(--accent-start))" />
            <stop offset="100%" stopColor="hsl(var(--accent-end))" />
          </linearGradient>
          <radialGradient id="hive-glow" cx="0.5" cy="0.55" r="0.6">
            <stop offset="0%" stopColor="hsl(var(--primary))" stopOpacity="0.28" />
            <stop offset="100%" stopColor="hsl(var(--primary))" stopOpacity="0" />
          </radialGradient>
        </defs>

        <rect width={VIEW_WIDTH} height={VIEW_HEIGHT} fill="hsl(var(--background))" />

        <g stroke="hsl(var(--foreground))" strokeWidth={1}>
          {CELLS.map((cell) => (
            <polygon
              key={cell.key}
              points={cell.points}
              fill={cell.accent ? "url(#hive-cell)" : "hsl(var(--foreground))"}
              fillOpacity={cell.accent ? 0.22 : cell.fillOpacity}
              strokeOpacity={cell.accent ? 0.45 : cell.strokeOpacity}
              className={cell.accent ? "hive-cell-pulse" : undefined}
              style={cell.accent ? { animationDelay: `${cell.pulseDelay}ms` } : undefined}
            />
          ))}
        </g>

        <ellipse
          cx={VIEW_WIDTH * 0.62}
          cy={VIEW_HEIGHT * 0.55}
          rx={VIEW_WIDTH * 0.45}
          ry={VIEW_HEIGHT * 0.6}
          fill="url(#hive-glow)"
        />
      </svg>
    </div>
  );
}
