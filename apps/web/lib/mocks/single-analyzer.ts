// Mock layer for Single Analyzer feature

const networkCopy: Record<string, string> = {
  instagram: "Instagram",
  tiktok: "TikTok",
  youtube: "YouTube"
};

export interface SingleAnalyzerResult {
  id: string;
  organizationId: string;
  clientId: string | null;
  clientName: string | null;
  inputMode: "url" | "search";
  inputUrl: string;
  searchQuery: string;
  networkSource: string;
  thumbnailUrl: string;
  videoUrl: string;
  title: string;
  description: string | null;
  author: string;
  views: number;
  likes: number;
  comments: number;
  strategyMode: string;
  videoAnalysis: string;
  scriptDraft: string;
  analyzedAtUtc: string;
  downloadedAtUtc: string | null;
  // Optional rich metadata sourced from the live scraping integration (Apify).
  contentType?: string;
  shares?: number;
  saves?: number;
  durationSeconds?: number | null;
  publishedAtUtc?: string | null;
  hashtags?: string[];
  dataSource?: "apify" | "mock";
  primaryHook?: string | null;
  isFallbackThumbnail?: boolean;
}

const analyzerSessions = new Map<string, SingleAnalyzerResult>();

function deriveNetworkFromUrl(url: string): string {
  try {
    const hostname = new URL(url).hostname.toLowerCase();
    if (hostname.includes("tiktok.com")) return "tiktok";
    if (hostname.includes("instagram.com")) return "instagram";
    if (hostname.includes("youtube.com") || hostname.includes("youtu.be")) return "youtube";
  } catch {
    // fall through
  }
  return "tiktok";
}

function slugify(text: string): string {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function buildResult(
  id: string,
  organizationId: string,
  clientId: string | null,
  clientName: string | null,
  inputMode: "url" | "search",
  inputUrl: string,
  searchQuery: string,
  networkSource: string,
  strategyMode: string,
  title: string,
  author: string,
  views: number,
  likes: number,
  comments: number,
  now: string
): SingleAnalyzerResult {
  return {
    id,
    organizationId,
    clientId,
    clientName,
    inputMode,
    inputUrl,
    searchQuery,
    networkSource,
    thumbnailUrl: `https://picsum.photos/seed/${encodeURIComponent(id)}/640/360`,
    videoUrl: inputUrl || `https://example.com/mock/${networkSource}/video-${id}`,
    title,
    description:
      "A compact mock description that mirrors scraped social captions. Longer live descriptions can be expanded in the analyzer card.",
    author,
    views,
    likes,
    comments,
    strategyMode,
    videoAnalysis: "",
    scriptDraft: "",
    analyzedAtUtc: now,
    downloadedAtUtc: null
  };
}

export function getMockVideoByUrl(
  organizationId: string,
  url: string,
  clientId: string | null = null,
  clientName: string | null = null
): SingleAnalyzerResult {
  const now = new Date().toISOString();
  const networkSource = deriveNetworkFromUrl(url);
  const id = `sa-url-${Date.now()}-${Math.floor(Math.random() * 1000)}`;
  const networkLabel = networkCopy[networkSource] ?? networkSource;

  const titles: Record<string, string> = {
    tiktok: "TikTok: How to build habits that actually stick",
    instagram: "Instagram: The mindset shift that changed everything",
    youtube: "YouTube: The productivity framework you've never tried"
  };

  return buildResult(
    id,
    organizationId,
    clientId,
    clientName,
    "url",
    url,
    "",
    networkSource,
    "educational",
    titles[networkSource] ?? `${networkLabel}: Content insight from URL`,
    `@creator_${networkSource}`,
    28000 + Math.floor(Math.random() * 12000),
    2100 + Math.floor(Math.random() * 800),
    310 + Math.floor(Math.random() * 200),
    now
  );
}

export function getMockVideoBySearch(
  organizationId: string,
  query: string,
  networkSource: string,
  strategyMode: string,
  clientId: string | null = null,
  clientName: string | null = null
): SingleAnalyzerResult {
  const now = new Date().toISOString();
  const id = `sa-search-${Date.now()}-${Math.floor(Math.random() * 1000)}`;
  const networkLabel = networkCopy[networkSource] ?? networkSource;
  const strategy = strategyMode || "informative";

  const suffix = strategy === "educational"
    ? "explained in 60 seconds"
    : strategy === "entertainment"
      ? "but make it fun"
      : "what nobody talks about";

  return buildResult(
    id,
    organizationId,
    clientId,
    clientName,
    "search",
    "",
    query,
    networkSource,
    strategy,
    `${networkLabel}: ${query} — ${suffix}`,
    `@best_match_${slugify(networkSource)}`,
    41000 + Math.floor(Math.random() * 20000),
    3200 + Math.floor(Math.random() * 1000),
    480 + Math.floor(Math.random() * 300),
    now
  );
}

export function analyzeMockVideo(organizationId: string, id: string): SingleAnalyzerResult {
  const existing = analyzerSessions.get(id);
  const base = existing ?? {
    id,
    organizationId,
    clientId: null,
    clientName: null,
    inputMode: "url" as const,
    inputUrl: "",
    searchQuery: "",
    networkSource: "tiktok",
    thumbnailUrl: `https://picsum.photos/seed/${encodeURIComponent(id)}/640/360`,
    videoUrl: `https://example.com/mock/tiktok/${id}`,
    title: "Mock video analysis",
    description: null,
    author: "@mock_creator",
    views: 25000,
    likes: 1950,
    comments: 290,
    strategyMode: "educational",
    videoAnalysis: "",
    scriptDraft: "",
    analyzedAtUtc: new Date().toISOString(),
    downloadedAtUtc: null
  };

  const networkLabel = networkCopy[base.networkSource] ?? base.networkSource;
  const clientContext = base.clientName
    ? `Client context: ${base.clientName} - adapt future mutations to this client's active brand strategy.`
    : "Client context: none - preserve the original video's positioning without client-specific orientation.";

  const analyzed: SingleAnalyzerResult = {
    ...base,
    videoAnalysis: [
      `Network source: ${networkLabel}`,
      clientContext,
      `Hook style: direct promise in the first 2 seconds — strong pattern interrupt.`,
      `Format angle: ${base.strategyMode} narrative with visual rhythm, B-roll cuts every 3s, and CTA pacing in the last 4s.`,
      `Engagement signals: ${base.views.toLocaleString()} views · ${base.likes.toLocaleString()} likes · ${base.comments.toLocaleString()} comments.`,
      `Retention markers: comment section shows strong "save" behavior; high ratio of saves to likes suggests evergreen appeal.`
    ].join("\n"),
    scriptDraft: [
      "1. Hook (0-3s): open with the strongest transformation or counterintuitive claim.",
      "2. Context (3-8s): why this matters for the audience right now.",
      "3. Main body (8-28s): 3 tactical points with one visual cue each — keep pacing tight.",
      "4. CTA (last 4s): save / share / comment with a single clear action verb."
    ].join("\n"),
    analyzedAtUtc: new Date().toISOString()
  };

  analyzerSessions.set(id, analyzed);
  return analyzed;
}

export function generateMockSingleDownload(
  organizationId: string,
  id: string,
  contentType: string
): { fileName: string; downloadUrl: string; status: string } {
  const session = analyzerSessions.get(id);
  const titleSlug = session ? slugify(session.title).slice(0, 48) : `video-${id}`;
  const fileName = `${titleSlug}-${contentType}.zip`;
  const downloadUrl = `/mock-downloads/single/${fileName}`;

  if (session) {
    analyzerSessions.set(id, { ...session, downloadedAtUtc: new Date().toISOString() });
  }

  return { fileName, downloadUrl, status: "simulated_ready" };
}

export function listMockSingleHistory(organizationId: string, limit = 12): SingleAnalyzerResult[] {
  return Array.from(analyzerSessions.values())
    .filter((item) => item.organizationId === organizationId)
    .sort((a, b) => new Date(b.analyzedAtUtc).getTime() - new Date(a.analyzedAtUtc).getTime())
    .slice(0, Math.max(1, limit));
}

export function recordMockAnalysis(result: SingleAnalyzerResult): void {
  analyzerSessions.set(result.id, result);
}

function seedDefaultHistory() {
  const orgId = "org-incoders-social";

  if (Array.from(analyzerSessions.values()).some((item) => item.organizationId === orgId)) {
    return;
  }

  const items: SingleAnalyzerResult[] = [
    {
      id: "sa-seed-001",
      organizationId: orgId,
      clientId: "client-wellness-lab",
      clientName: "Wellness Lab",
      inputMode: "url",
      inputUrl: "https://www.tiktok.com/@creator/video/123456",
      searchQuery: "",
      networkSource: "tiktok",
      thumbnailUrl: "https://picsum.photos/seed/sa-seed-001/640/360",
      videoUrl: "https://www.tiktok.com/@creator/video/123456",
      title: "TikTok: 3 productivity habits that changed my output",
      description:
        "Three practical habits for building a productivity system that survives busy weeks, client interruptions, and the usual motivation drop after day three.",
      author: "@daily_growth",
      views: 87400,
      likes: 6820,
      comments: 954,
      strategyMode: "educational",
      videoAnalysis: [
        "Network source: TikTok",
        "Hook style: bold opener — 'I wasted 2 years on the wrong system'.",
        "Format angle: educational narrative, 3-point structure, tight cut rhythm.",
        "Engagement signals: 87,400 views · 6,820 likes · 954 comments.",
        "Retention markers: high comment-to-like ratio suggests strong debate appeal."
      ].join("\n"),
      scriptDraft: [
        "1. Hook (0-3s): bold personal failure statement.",
        "2. Context (3-8s): productivity myth vs. reality.",
        "3. Main body (8-28s): 3 habits with one visual each.",
        "4. CTA (last 4s): 'Follow for the full framework.'"
      ].join("\n"),
      analyzedAtUtc: "2026-05-14T10:30:00.000Z",
      downloadedAtUtc: "2026-05-14T10:45:00.000Z"
    },
    {
      id: "sa-seed-002",
      organizationId: orgId,
      clientId: "client-wellness-lab",
      clientName: "Wellness Lab",
      inputMode: "search",
      inputUrl: "",
      searchQuery: "morning routine habits",
      networkSource: "instagram",
      thumbnailUrl: "https://picsum.photos/seed/sa-seed-002/640/360",
      videoUrl: "https://example.com/mock/instagram/seed-002",
      title: "Instagram: morning routine habits — what nobody talks about",
      description:
        "A calm morning routine breakdown focused on the hidden setup work that makes the visible habits easier to repeat.",
      author: "@wellness_lab_ig",
      views: 43200,
      likes: 3370,
      comments: 502,
      strategyMode: "informative",
      videoAnalysis: [
        "Network source: Instagram",
        "Hook style: curiosity gap — 'The habit nobody shows in their morning routine'.",
        "Format angle: informative reel with calm voiceover and B-roll lifestyle shots.",
        "Engagement signals: 43,200 views · 3,370 likes · 502 comments.",
        "Retention markers: high save rate — evergreen instructional format."
      ].join("\n"),
      scriptDraft: [
        "1. Hook (0-3s): curiosity gap about hidden morning behavior.",
        "2. Context (3-8s): why this detail is skipped in popular content.",
        "3. Main body (8-28s): 3 actionable steps with lifestyle visuals.",
        "4. CTA (last 4s): 'Save this for tomorrow morning.'"
      ].join("\n"),
      analyzedAtUtc: "2026-05-16T08:15:00.000Z",
      downloadedAtUtc: null
    },
    {
      id: "sa-seed-003",
      organizationId: orgId,
      clientId: "client-growth-studio",
      clientName: "Growth Studio",
      inputMode: "search",
      inputUrl: "",
      searchQuery: "content creation for beginners",
      networkSource: "youtube",
      thumbnailUrl: "https://picsum.photos/seed/sa-seed-003/640/360",
      videoUrl: "https://example.com/mock/youtube/seed-003",
      title: "YouTube: content creation for beginners — explained in 60 seconds",
      description:
        "A fast beginner-friendly creator framework with simple tools, clear pacing, and an intentionally lightweight production setup.",
      author: "@growth_studio_yt",
      views: 61800,
      likes: 4810,
      comments: 720,
      strategyMode: "entertainment",
      videoAnalysis: [
        "Network source: YouTube",
        "Hook style: fast-cut intro with energetic music — 'You only need 3 things to start.'",
        "Format angle: entertainment-first Short with quick tips and humor.",
        "Engagement signals: 61,800 views · 4,810 likes · 720 comments.",
        "Retention markers: very high like-to-view ratio (7.8%) — broad appeal."
      ].join("\n"),
      scriptDraft: [
        "1. Hook (0-3s): energetic 'You only need 3 things' opener.",
        "2. Context (3-8s): why beginners overthink it.",
        "3. Main body (8-28s): 3 tools / tips with punchy visuals.",
        "4. CTA (last 4s): 'Subscribe for weekly creator tips.'"
      ].join("\n"),
      analyzedAtUtc: "2026-05-17T14:55:00.000Z",
      downloadedAtUtc: null
    }
  ];

  for (const item of items) {
    analyzerSessions.set(item.id, item);
  }
}

seedDefaultHistory();
