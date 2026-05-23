import { NextResponse } from "next/server";
import { mapApifyTikTokItem, type ApifyTikTokItem } from "@/lib/api/single-analyzer";

// ── POC scraping integration ─────────────────────────────────────────────────
// Server-only route handler. The Apify token lives here (never shipped to the
// browser bundle). This is intentionally hardcoded for the proof of concept.
// TODO: replace with the encrypted integration-config flow before production and
// ROTATE this token — it is committed to git history.
const APIFY_TOKEN = process.env.APIFY_TOKEN;
const APIFY_TIKTOK_ACTOR = "GdWCkxBtKWOsKjdch";

export const runtime = "nodejs";
export const maxDuration = 120;

export async function POST(request: Request) {
  let body: { url?: string };
  try {
    body = (await request.json()) as { url?: string };
  } catch {
    return NextResponse.json({ error: "Invalid JSON body." }, { status: 400 });
  }

  const url = body.url?.trim();
  if (!url) {
    return NextResponse.json({ error: "A video URL is required." }, { status: 400 });
  }

  if (!APIFY_TOKEN) {
    return NextResponse.json({ error: "Apify is not configured for this route." }, { status: 503 });
  }

  const input = {
    postURLs: [url],
    resultsPerPage: 1,
    shouldDownloadVideos: false,
    shouldDownloadCovers: false,
    shouldDownloadSlideshowImages: false,
    shouldDownloadAvatars: false,
    shouldDownloadMusicCovers: false,
    shouldDownloadSubtitles: false,
    proxyCountryCode: "None",
  };

  const endpoint =
    `https://api.apify.com/v2/acts/${APIFY_TIKTOK_ACTOR}/run-sync-get-dataset-items` +
    `?token=${encodeURIComponent(APIFY_TOKEN)}`;

  let apifyResponse: Response;
  try {
    apifyResponse = await fetch(endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(input),
      cache: "no-store",
    });
  } catch {
    return NextResponse.json({ error: "Could not reach the scraping service." }, { status: 502 });
  }

  if (!apifyResponse.ok) {
    return NextResponse.json(
      { error: `Scraping service returned HTTP ${apifyResponse.status}.` },
      { status: 502 }
    );
  }

  let items: ApifyTikTokItem[];
  try {
    items = (await apifyResponse.json()) as ApifyTikTokItem[];
  } catch {
    return NextResponse.json({ error: "Scraping service returned an unreadable response." }, { status: 502 });
  }

  if (!Array.isArray(items) || items.length === 0) {
    return NextResponse.json({ error: "No video data found for that URL." }, { status: 404 });
  }

  return NextResponse.json(mapApifyTikTokItem(items[0], url));
}
