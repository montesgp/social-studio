/**
 * Regenerates app/apple-icon.png from the Social Studio mark.
 *
 * WHY A CHECKED-IN PNG: Safari on iOS ignores SVG apple-touch-icons, so app/icon.svg cannot cover
 * the home-screen case. This is the one rendition of the mark that has to ship as a raster.
 *
 * WHY A SCRIPT: so the raster stays regenerable when the brand colours move, instead of becoming an
 * orphan binary nobody can reproduce. It is NOT part of `next build` — run it by hand:
 *
 *     node scripts/generate-apple-icon.mjs
 *
 * DESIGN CONSTRAINTS specific to the Apple icon, which is why this is not just a resize of icon.svg:
 *   - Opaque background. iOS composites transparency onto black and the mark would lose its ground.
 *   - Artwork inset from the edges. iOS applies its own rounded/squircle mask and clips the corners,
 *     so the cell is kept well inside the safe area rather than full-bleed.
 *   - No connector wiring, same reason as the favicon: it does not survive the size.
 */

import { writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SIZE = 180;
const OUTPUT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "app", "apple-icon.png");

/** Dark theme --background (hsl 222 47% 11%), so the icon sits in the same world as the app. */
const BACKGROUND = "#0F172A";

/*
 * The mark is authored in the favicon's 32-unit space and scaled up, so both rasters stay the same
 * drawing. scale(4) => a 96x112 cell, translate(26,26) centres it inside 180 with room for the mask.
 */
const SVG = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${SIZE} ${SIZE}" width="${SIZE}" height="${SIZE}">
  <defs>
    <linearGradient id="cell" x1="0" y1="0" x2="0.35" y2="1">
      <stop offset="0%" stop-color="#A855F7" />
      <stop offset="100%" stop-color="#7C3AED" />
    </linearGradient>
  </defs>
  <rect width="${SIZE}" height="${SIZE}" fill="${BACKGROUND}" />
  <g transform="translate(26,26) scale(4)">
    <polygon points="16,2 28,9 28,23 16,30 4,23 4,9" fill="url(#cell)" />
    <polygon points="16,9.5 22.5,13.25 22.5,20.75 16,24.5 9.5,20.75 9.5,13.25" fill="#FFFFFF" fill-opacity="0.92" />
    <polygon points="16,9.5 22.5,13.25 16,17 9.5,13.25" fill="#FFFFFF" />
  </g>
</svg>`;

let sharp;
try {
  ({ default: sharp } = await import("sharp"));
} catch {
  console.error(
    "sharp is not resolvable. It normally arrives with Next's install; run `npm install sharp` " +
      "before regenerating the Apple icon."
  );
  process.exit(1);
}

const png = await sharp(Buffer.from(SVG)).png({ compressionLevel: 9 }).toBuffer();
await writeFile(OUTPUT, png);

const { width, height, channels } = await sharp(png).metadata();
console.log(`Wrote ${OUTPUT} (${width}x${height}, ${channels} channels, ${png.length} bytes)`);
