// Client + mapping layer for the Single Analyzer scraping integration.
// The provider-specific (Apify) shape is normalized into ScrapedVideo so the
// rest of the app never depends on Apify field names.

export interface ScrapedVideo {
  networkSource: string;
  contentType: string;
  sourceUrl: string;
  title: string;
  author: string;
  thumbnailUrl: string;
  videoUrl: string;
  views: number;
  likes: number;
  comments: number;
  shares: number;
  saves: number;
  durationSeconds: number | null;
  publishedAtUtc: string | null;
  hashtags: string[];
}

// Subset of the Apify TikTok Scraper item we consume. All optional — the actor
// may omit fields depending on the post.
export interface ApifyTikTokItem {
  id?: string;
  text?: string;
  webVideoUrl?: string;
  playCount?: number;
  diggCount?: number;
  commentCount?: number;
  shareCount?: number;
  collectCount?: number;
  createTimeISO?: string;
  isSlideshow?: boolean;
  authorMeta?: { name?: string; nickName?: string; avatar?: string };
  videoMeta?: { coverUrl?: string; originalCoverUrl?: string; duration?: number };
  hashtags?: Array<{ name?: string }>;
}

function toNumber(value: number | undefined): number {
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

export function mapApifyTikTokItem(item: ApifyTikTokItem, sourceUrl: string): ScrapedVideo {
  const author = item.authorMeta?.nickName || item.authorMeta?.name || "Unknown creator";
  const thumbnailUrl =
    item.videoMeta?.coverUrl || item.videoMeta?.originalCoverUrl || item.authorMeta?.avatar || "";

  return {
    networkSource: "tiktok",
    contentType: item.isSlideshow ? "slideshow" : "video",
    sourceUrl: item.webVideoUrl || sourceUrl,
    title: item.text?.trim() || "Untitled TikTok video",
    author,
    thumbnailUrl,
    videoUrl: item.webVideoUrl || sourceUrl,
    views: toNumber(item.playCount),
    likes: toNumber(item.diggCount),
    comments: toNumber(item.commentCount),
    shares: toNumber(item.shareCount),
    saves: toNumber(item.collectCount),
    durationSeconds: typeof item.videoMeta?.duration === "number" ? item.videoMeta.duration : null,
    publishedAtUtc: item.createTimeISO ?? null,
    hashtags: (item.hashtags ?? [])
      .map((h) => h.name?.trim())
      .filter((name): name is string => Boolean(name)),
  };
}

export async function scrapeVideoByUrl(url: string): Promise<ScrapedVideo> {
  const response = await fetch("/api/single-analyzer/scrape", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ url }),
  });

  if (!response.ok) {
    let message = `Scrape failed (HTTP ${response.status}).`;
    try {
      const problem = (await response.json()) as { error?: string };
      if (problem.error) message = problem.error;
    } catch {
      // keep default message
    }
    throw new Error(message);
  }

  return (await response.json()) as ScrapedVideo;
}
