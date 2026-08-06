export interface ReelScriptScene {
  timestamp?: string;
  voiceover?: string;
  onScreenText?: string;
}

export interface ReelScript {
  hook?: string;
  scenes?: ReelScriptScene[];
  ctaText?: string;
  suggestedMusicMood?: string;
}

/**
 * Parses a text item's body as a structured reel script.
 *
 * Text-medium items store their body as JSON when the generator produced a structured script, and
 * as plain prose otherwise. Returns null for anything that is not a scene-bearing script so callers
 * can fall back to rendering the raw text.
 */
export function tryParseReelScript(text: string): ReelScript | null {
  try {
    const parsed = JSON.parse(text) as ReelScript;
    return parsed && Array.isArray(parsed.scenes) ? parsed : null;
  } catch {
    return null;
  }
}

/**
 * Flattens a script into ordered display lines for compact previews (Content Bank cards), where
 * there is no room for the full cue-per-row layout the Studio detail pane uses.
 */
export function scriptPreviewLines(script: ReelScript): string[] {
  const lines: string[] = [];
  if (script.hook) lines.push(script.hook);
  for (const [index, scene] of (script.scenes ?? []).entries()) {
    const cue = scene.voiceover || scene.onScreenText;
    if (!cue) continue;
    lines.push(`${scene.timestamp ?? `#${index + 1}`} ${cue}`);
  }
  if (script.ctaText) lines.push(script.ctaText);
  return lines;
}

/**
 * Best-effort readable snippet for ANY text item: a parsed script's flattened lines, otherwise the
 * raw body. Used where only a couple of lines fit.
 */
export function textPreviewLines(text: string): string[] {
  const script = tryParseReelScript(text);
  if (script) return scriptPreviewLines(script);
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}
