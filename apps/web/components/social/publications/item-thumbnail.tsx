"use client";

import { useEffect, useRef, useState } from "react";
import { FileText, ImageOff, Loader2 } from "lucide-react";
import type { ContentBankItemResponse } from "@/lib/api/publications";
import { fetchContentItemAsset } from "@/lib/api/content-studio";
import { resolveBankThumbnail } from "@/lib/publications/thumbnail";
import { cn } from "@/lib/utils";
import { formatLabel } from "@/lib/format/format-key";
import { textPreviewEntries } from "@/lib/publications/reel-script";

/**
 * Readable preview for a text/script item, mirroring what Content Studio shows in its detail pane.
 * `compact` drops to a single line for the small Planner cards.
 */
function TextPreview({ item, compact }: { item: ContentBankItemResponse; compact: boolean }) {
  const body = item.text?.trim() || item.caption?.trim() || "";
  const lines = body ? textPreviewEntries(body) : [];

  const viewportRef = useRef<HTMLDivElement | null>(null);
  const copyRef = useRef<HTMLDivElement | null>(null);
  // Only scroll when the script genuinely does not fit. A body SHORTER than the tile would otherwise
  // still travel -50%, dragging a visible empty gap through the card for half of every cycle.
  const [overflows, setOverflows] = useState(false);

  useEffect(() => {
    const viewport = viewportRef.current;
    const copy = copyRef.current;
    if (!viewport || !copy) return;

    const measure = () => setOverflows(copy.offsetHeight > viewport.clientHeight);
    measure();

    if (typeof ResizeObserver === "undefined") return;
    // Fonts load late and the grid reflows on resize; both change the answer.
    const observer = new ResizeObserver(measure);
    observer.observe(viewport);
    observer.observe(copy);
    return () => observer.disconnect();
  }, [body, compact]);

  if (lines.length === 0) {
    return (
      <div className="flex h-full w-full flex-col items-center justify-center gap-1.5 p-2 text-center">
        <FileText className="h-5 w-5 text-muted-foreground" />
        {!compact ? (
          <span className="text-[11px] font-medium text-muted-foreground">{formatLabel(item.formatKey)}</span>
        ) : null}
      </div>
    );
  }

  // Longer scripts scroll for longer, so the pace stays readable instead of racing on a long body.
  const durationSeconds = Math.min(60, Math.max(14, lines.length * 3));

  return (
    <div className="teleprompter relative flex h-full max-h-full w-full flex-col overflow-hidden bg-muted/30 text-left">
      {!compact ? (
        // Centred, with symmetric `px-9` gutters that clear the card's absolutely-positioned
        // selection checkbox on the left. Left-aligned it sat right beside that checkbox and read as
        // its label. The format itself is humanized — "Reel Script", not the `reel-script` slug.
        <div className="flex shrink-0 items-center justify-center gap-1.5 border-b border-border/60 bg-background/70 px-9 py-1.5">
          <FileText className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
          <span className="truncate text-[11px] font-semibold tracking-wide text-foreground/80">
            {formatLabel(item.formatKey)}
          </span>
        </div>
      ) : null}

      <div ref={viewportRef} className="relative min-h-0 flex-1 overflow-hidden px-2.5 py-2">
        <div
          className={cn("flex flex-col", overflows && "teleprompter-track")}
          style={overflows ? { animationDuration: `${durationSeconds}s` } : undefined}
        >
          {/* Two IDENTICAL copies, and the track travels exactly -50%: the instant the first copy
              clears the top, the second sits precisely where the first started, so the opening scenes
              re-enter from the bottom with no visible reset. The copies carry their own trailing
              padding instead of a gap between them — a gap would offset the halfway point and make
              the loop jump. */}
          {(overflows ? [0, 1] : [0]).map((copy) => (
            <div
              key={copy}
              ref={copy === 0 ? copyRef : undefined}
              className="flex shrink-0 flex-col gap-2 pb-2"
              aria-hidden={copy === 1}
            >
              {lines.map((line, index) => (
                <p
                  key={index}
                  className={cn(
                    "whitespace-pre-wrap break-words leading-snug text-foreground/90",
                    compact ? "text-[10px]" : "text-[13px]",
                  )}
                >
                  {line.cue ? (
                    <span className="mr-1 font-bold tabular-nums text-foreground">{line.cue}</span>
                  ) : null}
                  {line.text}
                </p>
              ))}
              <div className="flex items-center gap-1.5 pt-0.5" aria-hidden="true">
                <span className="h-px flex-1 bg-border" />
                <span className="text-[9px] font-semibold uppercase tracking-widest text-muted-foreground">
                  End
                </span>
                <span className="h-px flex-1 bg-border" />
              </div>
            </div>
          ))}
        </div>

        {/* Fades the top and bottom edges so lines enter and leave instead of being cut mid-glyph. */}
        <div className="pointer-events-none absolute inset-x-0 top-0 h-4 bg-gradient-to-b from-muted/60 to-transparent" />
        <div className="pointer-events-none absolute inset-x-0 bottom-0 h-4 bg-gradient-to-t from-muted/60 to-transparent" />
      </div>
    </div>
  );
}

/**
 * Renders an item's thumbnail: the resolved markup/background image when one exists, otherwise the
 * first raw asset (fetched the same way Content Studio's `AssetFallbackImage` does), otherwise a
 * readable text/script preview instead of a broken-image icon.
 *
 * Shared by the Bank grid and the Planner board so the SAME piece always looks the same wherever it
 * is shown — the Planner used to render a bare format key, leaving no way to tell which piece was
 * about to be published.
 */
export function ItemThumbnail({
  item,
  token,
  compact = false,
}: {
  item: ContentBankItemResponse;
  token: string;
  compact?: boolean;
}) {
  const markupThumbnail = resolveBankThumbnail(item.visualSpecMarkup);
  const [assetUrl, setAssetUrl] = useState<string | null>(null);
  // Three-way, because "no image yet" and "this image will never arrive" must not look the same.
  // The grid used to render the text fallback while the asset request was still in flight, so a slow
  // piece looked permanently broken until it silently swapped to the image.
  const [assetState, setAssetState] = useState<"idle" | "loading" | "failed">("idle");
  // Text items render their body, never an asset: a script's stored asset is a .md/.txt file, and
  // pointing an <img>/background at it yields a broken image rather than a preview.
  const isText = item.medium === "Text";
  const firstAsset = isText ? undefined : item.assets[0];

  // Viewport gate. Every card used to fire its asset request on mount, so a client with a hundred
  // pieces opened a hundred concurrent authenticated fetches the moment the Bank loaded — the
  // browser queued them and the slowest ones lost, which is why a preview sometimes never appeared
  // even though the listing had finished. Only cards at or near the viewport fetch now.
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [inViewport, setInViewport] = useState(false);

  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;

    // No IntersectionObserver (jsdom, very old browsers) means no gate rather than no image.
    if (typeof IntersectionObserver === "undefined") {
      setInViewport(true);
      return;
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          setInViewport(true);
          observer.disconnect();
        }
      },
      // Start a screenful early so a scrolling operator meets a loaded image, not a spinner.
      { rootMargin: "600px" },
    );

    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    setAssetUrl(null);

    if (markupThumbnail || !firstAsset) {
      setAssetState("idle");
      return;
    }

    if (!inViewport) {
      // Off-screen and not yet requested: hold the loading affordance rather than the text preview,
      // which would read as "this piece has no image".
      setAssetState("loading");
      return;
    }

    if (/^(https?:|data:|blob:|\/)/i.test(firstAsset.storageUri)) {
      setAssetUrl(firstAsset.storageUri);
      setAssetState("idle");
      return;
    }

    let cancelled = false;
    let objectUrl: string | null = null;
    setAssetState("loading");
    fetchContentItemAsset(item.id, 0, token)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setAssetUrl(objectUrl);
        setAssetState("idle");
      })
      .catch(() => {
        // The piece claims an asset the storage cannot serve: the blob is gone, or the reference is
        // stale. That is a BROKEN piece, not a text piece, so it must not fall through to the text
        // preview — the operator has to be able to tell it apart and purge it.
        if (!cancelled) {
          setAssetUrl(null);
          setAssetState("failed");
        }
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item.id, markupThumbnail, firstAsset?.storageUri, token, inViewport]);

  const thumbnail = isText ? null : markupThumbnail ?? assetUrl;

  return (
    <div ref={containerRef} className="h-full w-full">
      {thumbnail ? (
        <div
          className="h-full w-full bg-cover bg-center"
          style={{ backgroundImage: `url(${thumbnail})` }}
          aria-hidden="true"
        />
      ) : assetState === "loading" ? (
        <div className="flex h-full w-full items-center justify-center bg-muted/40" aria-label="Loading preview">
          <Loader2
            className={cn(
              "animate-spin text-muted-foreground",
              compact ? "h-3.5 w-3.5" : "h-5 w-5",
            )}
          />
        </div>
      ) : assetState === "failed" ? (
        <div className="flex h-full w-full flex-col items-center justify-center gap-1 bg-muted/40 p-2 text-center">
          <ImageOff className={cn("text-muted-foreground", compact ? "h-4 w-4" : "h-5 w-5")} />
          {!compact ? (
            <span className="text-[10px] leading-tight text-muted-foreground">Preview unavailable</span>
          ) : null}
        </div>
      ) : (
        <TextPreview item={item} compact={compact} />
      )}
    </div>
  );
}
