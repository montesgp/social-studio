"use client";

import { useEffect, useState } from "react";
import { FileText } from "lucide-react";
import type { ContentBankItemResponse } from "@/lib/api/publications";
import { fetchContentItemAsset } from "@/lib/api/content-studio";
import { resolveBankThumbnail } from "@/lib/publications/thumbnail";
import { textPreviewLines } from "@/lib/publications/reel-script";

/**
 * Readable preview for a text/script item, mirroring what Content Studio shows in its detail pane.
 * `compact` drops to a single line for the small Planner cards.
 */
function TextPreview({ item, compact }: { item: ContentBankItemResponse; compact: boolean }) {
  const body = item.text?.trim() || item.caption?.trim() || "";
  const lines = body ? textPreviewLines(body) : [];
  const maxLines = compact ? 2 : 5;

  if (lines.length === 0) {
    return (
      <div className="flex h-full w-full flex-col items-center justify-center gap-1.5 p-2 text-center">
        <FileText className="h-4 w-4 text-muted-foreground" />
        {!compact ? <span className="text-[11px] text-muted-foreground">{item.formatKey}</span> : null}
      </div>
    );
  }

  return (
    <div className="flex h-full w-full flex-col gap-1 overflow-hidden p-2 text-left">
      {!compact ? (
        <span className="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
          <FileText className="h-3 w-3" /> {item.formatKey}
        </span>
      ) : null}
      <div className="flex flex-col gap-0.5 overflow-hidden">
        {lines.slice(0, maxLines).map((line, index) => (
          <p key={index} className="truncate text-[10px] leading-snug text-foreground/80">
            {line}
          </p>
        ))}
      </div>
      {!compact && lines.length > maxLines ? (
        <span className="text-[10px] text-muted-foreground">+{lines.length - maxLines} more</span>
      ) : null}
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
  // Text items render their body, never an asset: a script's stored asset is a .md/.txt file, and
  // pointing an <img>/background at it yields a broken image rather than a preview.
  const isText = item.medium === "Text";
  const firstAsset = isText ? undefined : item.assets[0];

  useEffect(() => {
    setAssetUrl(null);
    if (markupThumbnail || !firstAsset) return;

    if (/^(https?:|data:|blob:|\/)/i.test(firstAsset.storageUri)) {
      setAssetUrl(firstAsset.storageUri);
      return;
    }

    let cancelled = false;
    let objectUrl: string | null = null;
    fetchContentItemAsset(item.id, 0, token)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setAssetUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setAssetUrl(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item.id, markupThumbnail, firstAsset?.storageUri, token]);

  const thumbnail = isText ? null : markupThumbnail ?? assetUrl;

  if (thumbnail) {
    return (
      <div
        className="h-full w-full bg-cover bg-center"
        style={{ backgroundImage: `url(${thumbnail})` }}
        aria-hidden="true"
      />
    );
  }

  return <TextPreview item={item} compact={compact} />;
}
