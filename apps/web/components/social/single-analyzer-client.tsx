"use client";

import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import {
  ScanSearch,
  Search,
  Link2,
  Download,
  Clock,
  RefreshCw,
  Film,
  ImageIcon,
  Megaphone,
  CheckCircle2,
  X,
  ChevronRight,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { getMockOrganizations } from "@/lib/mocks/content-workflow";
import type { Organization } from "@/lib/api/types";
import { useActiveOrg } from "@/lib/auth/active-org-context";
import {
  getMockVideoByUrl,
  getMockVideoBySearch,
  analyzeMockVideo,
  generateMockSingleDownload,
  listMockSingleHistory,
  recordMockAnalysis,
  type SingleAnalyzerResult,
} from "@/lib/mocks/single-analyzer";

// ── Constants ──────────────────────────────────────────────────────────────────

const strategyColors: Record<string, string> = {
  educational: "bg-blue-500/15 text-blue-300 border-blue-500/30",
  informative: "bg-cyan-500/15 text-cyan-300 border-cyan-500/30",
  entertainment: "bg-amber-500/15 text-amber-300 border-amber-500/30",
};

const networkOptions = [
  { value: "tiktok", label: "TikTok" },
  { value: "instagram", label: "Instagram" },
  { value: "youtube", label: "YouTube" },
];

const strategyOptions = [
  { value: "educational", label: "Educational" },
  { value: "informative", label: "Informative" },
  { value: "entertainment", label: "Entertainment" },
];

function sleep(ms: number) {
  return new Promise<void>((resolve) => setTimeout(resolve, ms));
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

// ── Local sub-components ───────────────────────────────────────────────────────

function Fact({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-xs text-muted-foreground/60 uppercase tracking-wider">
        {label}
      </span>
      <span className="text-sm font-medium text-foreground/90">{value}</span>
    </div>
  );
}

type TabOption = { key: string; label: string };

function TabBar({
  tabs,
  active,
  onChange,
}: {
  tabs: TabOption[];
  active: string;
  onChange: (key: string) => void;
}) {
  return (
    <div className="flex items-center gap-1 rounded-lg bg-muted/40 p-1 w-fit">
      {tabs.map((tab) => (
        <button
          key={tab.key}
          type="button"
          onClick={() => onChange(tab.key)}
          className={cn(
            "rounded-md px-3 py-1.5 text-xs font-medium transition-colors",
            active === tab.key
              ? "bg-background text-foreground shadow-sm"
              : "text-muted-foreground hover:text-foreground"
          )}
        >
          {tab.label}
        </button>
      ))}
    </div>
  );
}

function InlineProgress({ value }: { value: number }) {
  return (
    <div className="h-1 w-full rounded-full bg-muted overflow-hidden">
      <div
        className="h-full bg-primary transition-all duration-150"
        style={{ width: `${Math.min(100, Math.max(0, value))}%` }}
      />
    </div>
  );
}

function SocialTabletPreview({
  content,
  network,
}: {
  content: string;
  network: string;
}) {
  const lines = content.split("\n").filter(Boolean);
  return (
    <div className="rounded-xl border border-border/40 bg-background/60 p-4 space-y-3 min-h-[200px]">
      <div className="flex items-center gap-2 pb-2 border-b border-border/30">
        <div className="w-8 h-8 rounded-full bg-gradient-to-br from-purple-500/40 to-blue-500/40 flex items-center justify-center">
          <span className="text-xs font-bold text-foreground/80">
            {network.charAt(0).toUpperCase()}
          </span>
        </div>
        <span className="text-xs font-medium text-muted-foreground capitalize">
          {network}
        </span>
      </div>
      <div className="space-y-1.5">
        {lines.slice(0, 8).map((line, i) => (
          <p key={i} className="text-sm text-foreground/80 leading-relaxed">
            {line}
          </p>
        ))}
        {lines.length > 8 && (
          <p className="text-xs text-muted-foreground/60 italic">
            +{lines.length - 8} more lines…
          </p>
        )}
      </div>
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────────

interface SingleAnalyzerClientProps {
  isPlatformAdmin: boolean;
}

export function SingleAnalyzerClient({ isPlatformAdmin }: SingleAnalyzerClientProps) {
  const { orgId: activeOrgId } = useActiveOrg();
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [selectedOrganizationId, setSelectedOrganizationId] = useState("");
  const [inputMode, setInputMode] = useState<"url" | "search">("url");
  const [urlInput, setUrlInput] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [searchNetwork, setSearchNetwork] = useState("tiktok");
  const [searchStrategy, setSearchStrategy] = useState("educational");
  const [videoResult, setVideoResult] = useState<SingleAnalyzerResult | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const [isAnalyzed, setIsAnalyzed] = useState(false);
  const [analyzingProgress, setAnalyzingProgress] = useState(0);
  const [generatedScripts, setGeneratedScripts] = useState<Record<string, string>>({});
  const [generatedCarousels, setGeneratedCarousels] = useState<Record<string, string>>({});
  const [generatedFlyers, setGeneratedFlyers] = useState<Record<string, string>>({});
  const [generatingConversion, setGeneratingConversion] = useState<string | null>(null);
  const [rightPanelTab, setRightPanelTab] = useState<"conversion" | "preview">("conversion");
  const [previewNetworkTab, setPreviewNetworkTab] = useState("tiktok");
  const [mutationMode, setMutationMode] = useState<"script" | "carousel" | "flyer">("script");
  const [historyItems, setHistoryItems] = useState<SingleAnalyzerResult[]>([]);
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState(0);
  const [downloadResult, setDownloadResult] = useState<{ fileName: string; downloadUrl: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const orgs = getMockOrganizations();
    setOrganizations(orgs);
    if (orgs.length > 0) setSelectedOrganizationId(orgs[0].id);
  }, []);

  useEffect(() => {
    if (selectedOrganizationId) {
      setHistoryItems(listMockSingleHistory(selectedOrganizationId));
    }
  }, [selectedOrganizationId]);

  useEffect(() => {
    if (!isPlatformAdmin && activeOrgId) {
      setSelectedOrganizationId(activeOrgId);
    }
  }, [isPlatformAdmin, activeOrgId]);

  function resetVideoState() {
    setVideoResult(null);
    setIsAnalyzed(false);
    setDownloadResult(null);
    setGeneratedScripts({});
    setGeneratedCarousels({});
    setGeneratedFlyers({});
    setError(null);
  }

  async function loadVideoByUrl() {
    if (!urlInput.trim()) return;
    setIsLoading(true);
    resetVideoState();
    await sleep(600);
    try {
      const result = getMockVideoByUrl(selectedOrganizationId, urlInput.trim());
      setVideoResult(result);
      setPreviewNetworkTab(result.networkSource);
    } catch {
      setError("Failed to load video. Check the URL and try again.");
    } finally {
      setIsLoading(false);
    }
  }

  async function findBestMatch() {
    if (!searchQuery.trim()) return;
    setIsLoading(true);
    resetVideoState();
    await sleep(700);
    try {
      const result = getMockVideoBySearch(
        selectedOrganizationId,
        searchQuery.trim(),
        searchNetwork,
        searchStrategy
      );
      setVideoResult(result);
      setPreviewNetworkTab(result.networkSource);
    } catch {
      setError("Search failed. Try a different keyword.");
    } finally {
      setIsLoading(false);
    }
  }

  async function runAnalysis() {
    if (!videoResult) return;
    setIsAnalyzing(true);
    setAnalyzingProgress(0);
    setIsAnalyzed(false);
    const start = Date.now();
    const duration = 3000;
    const interval = setInterval(() => {
      const elapsed = Date.now() - start;
      setAnalyzingProgress(Math.min(Math.floor((elapsed / duration) * 100), 95));
    }, 80);
    await sleep(duration);
    clearInterval(interval);
    const analyzed = analyzeMockVideo(selectedOrganizationId, videoResult.id);
    recordMockAnalysis(analyzed);
    setAnalyzingProgress(100);
    await sleep(300);
    setVideoResult(analyzed);
    setIsAnalyzing(false);
    setIsAnalyzed(true);
    setHistoryItems(listMockSingleHistory(selectedOrganizationId));
  }

  function buildScriptContent(network: string) {
    if (!videoResult) return "";
    return [
      `── ${network.toUpperCase()} SCRIPT ──`,
      videoResult.scriptDraft || "(Run analysis to generate script)",
      "",
      `Hook adapted for ${network}: lead with the strongest transformation.`,
      `CTA: Follow on ${network} for more insights.`,
    ].join("\n");
  }

  function buildCarouselContent(network: string) {
    if (!videoResult) return "";
    const points = videoResult.videoAnalysis
      ? videoResult.videoAnalysis.split("\n").filter(Boolean).slice(1, 4).map((l) => `→ ${l}`)
      : ["→ (Analyze video to generate key points)"];
    return [
      `📱 ${network.toUpperCase()} CAROUSEL`,
      "",
      "[ Slide 1 — Cover ]",
      videoResult.title,
      "",
      "[ Slides 2–4 — Key Points ]",
      ...points,
      "",
      "[ Final Slide — CTA ]",
      `Follow on ${network} for more quality content.`,
    ].join("\n");
  }

  function buildFlyerContent(network: string) {
    if (!videoResult) return "";
    return [
      `🖼️ ${network.toUpperCase()} FLYER`,
      "══════════════",
      `HEADLINE: ${videoResult.title.toUpperCase().slice(0, 50)}`,
      "",
      `AUTHOR: ${videoResult.author}`,
      `VIEWS: ${videoResult.views.toLocaleString()} · LIKES: ${videoResult.likes.toLocaleString()}`,
      "",
      "FOOTER: Follow us for weekly insights.",
    ].join("\n");
  }

  async function handleGenerate(network: string) {
    if (mutationMode === "script" && generatedScripts[network]) return;
    if (mutationMode === "carousel" && generatedCarousels[network]) return;
    if (mutationMode === "flyer" && generatedFlyers[network]) return;
    const key = `${network}-${mutationMode}`;
    setGeneratingConversion(key);
    await sleep(mutationMode === "carousel" ? 1100 : mutationMode === "flyer" ? 800 : 900);
    if (mutationMode === "script") {
      setGeneratedScripts((prev) => ({ ...prev, [network]: buildScriptContent(network) }));
    } else if (mutationMode === "carousel") {
      setGeneratedCarousels((prev) => ({ ...prev, [network]: buildCarouselContent(network) }));
    } else {
      setGeneratedFlyers((prev) => ({ ...prev, [network]: buildFlyerContent(network) }));
    }
    setGeneratingConversion(null);
  }

  async function handleDownload() {
    if (!videoResult) return;
    setIsDownloading(true);
    setDownloadProgress(0);
    setDownloadResult(null);
    const start = Date.now();
    const interval = setInterval(() => {
      setDownloadProgress(Math.min(Math.floor(((Date.now() - start) / 2500) * 100), 90));
    }, 80);
    await sleep(2500);
    clearInterval(interval);
    const result = generateMockSingleDownload(selectedOrganizationId, videoResult.id, "all");
    setDownloadProgress(100);
    await sleep(200);
    setDownloadResult(result);
    setIsDownloading(false);
  }

  function activeContent(network: string): string | null {
    if (mutationMode === "script") return generatedScripts[network] ?? null;
    if (mutationMode === "carousel") return generatedCarousels[network] ?? null;
    return generatedFlyers[network] ?? null;
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col gap-6 p-6 max-w-screen-xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="rounded-lg bg-purple-500/10 p-2.5">
            <ScanSearch className="h-5 w-5 text-purple-400" />
          </div>
          <div>
            <h1 className="text-xl font-semibold text-foreground">Single Analyzer</h1>
            <p className="text-sm text-muted-foreground">Load any video · analyze · convert</p>
          </div>
        </div>
        {isPlatformAdmin && organizations.length > 0 && (
          <select
            className="h-8 min-w-[12rem] rounded-md border border-border bg-background px-3 text-xs"
            value={selectedOrganizationId}
            onChange={(e) => setSelectedOrganizationId(e.target.value)}
          >
            {organizations.map((org) => (
              <option key={org.id} value={org.id}>{org.name}</option>
            ))}
          </select>
        )}
      </div>

      {/* Error banner */}
      {error && (
        <div className="flex items-center gap-3 rounded-lg border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-700 dark:text-red-200">
          <X className="h-4 w-4 shrink-0" />
          <span>{error}</span>
          <button type="button" className="ml-auto opacity-70 hover:opacity-100" onClick={() => setError(null)}>
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}

      {/* Main 2-col grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* ── LEFT PANEL ── */}
        <div className="flex flex-col gap-4">
          {/* Input card */}
          <Card className="border-border/40 bg-background/60">
            <CardHeader className="pb-3">
              <TabBar
                tabs={[
                  { key: "url", label: "By URL" },
                  { key: "search", label: "By Search" },
                ]}
                active={inputMode}
                onChange={(k) => setInputMode(k as "url" | "search")}
              />
            </CardHeader>
            <CardContent className="pt-0 space-y-3">
              {inputMode === "url" ? (
                <div className="flex gap-2">
                  <Input
                    placeholder="Paste TikTok, Instagram or YouTube URL…"
                    value={urlInput}
                    onChange={(e) => setUrlInput(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && void loadVideoByUrl()}
                    className="h-8 text-xs"
                  />
                  <Button
                    size="sm"
                    onClick={() => void loadVideoByUrl()}
                    disabled={isLoading || !urlInput.trim()}
                    className="h-8 px-3 text-xs"
                  >
                    {isLoading ? <RefreshCw className="h-3 w-3 animate-spin" /> : "Load"}
                  </Button>
                </div>
              ) : (
                <div className="space-y-2">
                  <div className="flex gap-2">
                    <select
                      className="h-8 w-32 rounded-md border border-border bg-background px-2 text-xs"
                      value={searchNetwork}
                      onChange={(e) => setSearchNetwork(e.target.value)}
                    >
                      {networkOptions.map((n) => (
                        <option key={n.value} value={n.value}>{n.label}</option>
                      ))}
                    </select>
                    <select
                      className="h-8 w-36 rounded-md border border-border bg-background px-2 text-xs"
                      value={searchStrategy}
                      onChange={(e) => setSearchStrategy(e.target.value)}
                    >
                      {strategyOptions.map((s) => (
                        <option key={s.value} value={s.value}>{s.label}</option>
                      ))}
                    </select>
                  </div>
                  <div className="flex gap-2">
                    <Input
                      placeholder="Search by keyword or topic…"
                      value={searchQuery}
                      onChange={(e) => setSearchQuery(e.target.value)}
                      onKeyDown={(e) => e.key === "Enter" && void findBestMatch()}
                      className="h-8 text-xs flex-1"
                    />
                    <Button
                      size="sm"
                      onClick={() => void findBestMatch()}
                      disabled={isLoading || !searchQuery.trim()}
                      className="h-8 px-3 text-xs"
                    >
                      {isLoading ? <RefreshCw className="h-3 w-3 animate-spin" /> : <Search className="h-3 w-3" />}
                    </Button>
                  </div>
                </div>
              )}
            </CardContent>
          </Card>

          {/* Video card */}
          {videoResult && (
            <Card className="border-border/40 bg-background/60 overflow-hidden">
              <div className="relative aspect-video w-full overflow-hidden bg-muted/30">
                <img
                  src={videoResult.thumbnailUrl}
                  alt={videoResult.title}
                  className="w-full h-full object-cover"
                />
                <div className="absolute top-2 right-2">
                  <Badge variant="outline" className="bg-background/80 text-xs capitalize">
                    {videoResult.networkSource}
                  </Badge>
                </div>
                {videoResult.strategyMode && (
                  <div className="absolute top-2 left-2">
                    <Badge className={cn("text-xs capitalize border", strategyColors[videoResult.strategyMode] ?? "")}>
                      {videoResult.strategyMode}
                    </Badge>
                  </div>
                )}
              </div>
              <CardContent className="p-4 space-y-3">
                <div>
                  <h3 className="text-sm font-semibold text-foreground line-clamp-2">{videoResult.title}</h3>
                  <p className="mt-0.5 text-xs text-muted-foreground">{videoResult.author}</p>
                </div>
                <div className="grid grid-cols-3 gap-2">
                  <Fact label="Views" value={videoResult.views.toLocaleString()} />
                  <Fact label="Likes" value={videoResult.likes.toLocaleString()} />
                  <Fact label="Comments" value={videoResult.comments.toLocaleString()} />
                </div>
                {!isAnalyzed ? (
                  <Button
                    className="w-full h-8 text-xs mt-1"
                    onClick={() => void runAnalysis()}
                    disabled={isAnalyzing}
                  >
                    {isAnalyzing ? (
                      <span className="flex items-center gap-2">
                        <RefreshCw className="h-3 w-3 animate-spin" />
                        Analyzing… {analyzingProgress}%
                      </span>
                    ) : (
                      "Analyze Video"
                    )}
                  </Button>
                ) : (
                  <div className="flex items-center gap-2 rounded-md bg-green-500/10 border border-green-500/30 px-3 py-2 text-xs text-green-600 dark:text-green-300">
                    <CheckCircle2 className="h-3.5 w-3.5" />
                    Analysis complete
                  </div>
                )}
                {isAnalyzing && <InlineProgress value={analyzingProgress} />}
              </CardContent>
            </Card>
          )}

          {/* Analysis detail */}
          {isAnalyzed && videoResult?.videoAnalysis && (
            <Card className="border-border/40 bg-background/60">
              <CardHeader className="pb-2">
                <CardTitle className="text-sm flex items-center gap-2">
                  <ChevronRight className="h-4 w-4 text-purple-400" />
                  Video Analysis
                </CardTitle>
              </CardHeader>
              <CardContent className="pt-0">
                <pre className="whitespace-pre-wrap text-xs text-muted-foreground font-mono leading-relaxed">
                  {videoResult.videoAnalysis}
                </pre>
              </CardContent>
            </Card>
          )}

          {/* Download */}
          {isAnalyzed && videoResult && (
            <Card className="border-border/40 bg-background/60">
              <CardHeader className="pb-2">
                <CardTitle className="text-sm flex items-center gap-2">
                  <Download className="h-4 w-4" />
                  Download
                </CardTitle>
              </CardHeader>
              <CardContent className="pt-0 space-y-3">
                {downloadResult ? (
                  <div className="space-y-2">
                    <div className="flex items-center gap-2 rounded-md bg-green-500/10 border border-green-500/30 px-3 py-2 text-xs text-green-600 dark:text-green-300">
                      <CheckCircle2 className="h-3.5 w-3.5" />
                      Ready: {downloadResult.fileName}
                    </div>
                    <Button
                      variant="outline"
                      size="sm"
                      className="w-full h-7 text-xs"
                      onClick={() => window.open(downloadResult.downloadUrl, "_blank")}
                    >
                      Open file
                    </Button>
                  </div>
                ) : (
                  <>
                    {isDownloading && <InlineProgress value={downloadProgress} />}
                    <Button
                      size="sm"
                      className="w-full h-8 text-xs"
                      onClick={() => void handleDownload()}
                      disabled={isDownloading}
                    >
                      {isDownloading ? `Preparing… ${downloadProgress}%` : "Download video + analysis"}
                    </Button>
                  </>
                )}
              </CardContent>
            </Card>
          )}
        </div>

        {/* ── RIGHT PANEL ── */}
        <div className="flex flex-col gap-4">
          {isAnalyzed ? (
            <Card className="border-border/40 bg-background/60 flex-1">
              <CardHeader className="pb-3">
                <TabBar
                  tabs={[
                    { key: "conversion", label: "Convert" },
                    { key: "preview", label: "Preview" },
                  ]}
                  active={rightPanelTab}
                  onChange={(k) => setRightPanelTab(k as "conversion" | "preview")}
                />
              </CardHeader>
              <CardContent className="pt-0">
                {rightPanelTab === "conversion" && (
                  <div className="space-y-4">
                    {/* Format mode selector */}
                    <div className="flex gap-1.5">
                      {(["script", "carousel", "flyer"] as const).map((mode) => (
                        <button
                          key={mode}
                          type="button"
                          onClick={() => setMutationMode(mode)}
                          className={cn(
                            "flex items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-medium border transition-colors capitalize",
                            mutationMode === mode
                              ? "border-purple-500/50 bg-purple-500/10 text-purple-400"
                              : "border-border/40 text-muted-foreground hover:text-foreground"
                          )}
                        >
                          {mode === "script" && <Film className="h-3 w-3" />}
                          {mode === "carousel" && <ImageIcon className="h-3 w-3" />}
                          {mode === "flyer" && <Megaphone className="h-3 w-3" />}
                          {mode}
                        </button>
                      ))}
                    </div>

                    {/* Per-network tabs */}
                    <div>
                      <TabBar
                        tabs={networkOptions.map((n) => ({ key: n.value, label: n.label }))}
                        active={previewNetworkTab}
                        onChange={setPreviewNetworkTab}
                      />
                      <div className="mt-3 space-y-3">
                        {activeContent(previewNetworkTab) ? (
                          <div className="rounded-lg border border-border/40 bg-muted/20 p-3">
                            <pre className="whitespace-pre-wrap text-xs text-foreground/80 font-mono leading-relaxed">
                              {activeContent(previewNetworkTab)}
                            </pre>
                          </div>
                        ) : (
                          <Button
                            size="sm"
                            variant="outline"
                            className="w-full h-8 text-xs"
                            onClick={() => void handleGenerate(previewNetworkTab)}
                            disabled={generatingConversion !== null}
                          >
                            {generatingConversion === `${previewNetworkTab}-${mutationMode}` ? (
                              <span className="flex items-center gap-2">
                                <RefreshCw className="h-3 w-3 animate-spin" />
                                Generating…
                              </span>
                            ) : (
                              `Generate ${mutationMode} for ${networkOptions.find((n) => n.value === previewNetworkTab)?.label ?? previewNetworkTab}`
                            )}
                          </Button>
                        )}
                      </div>
                    </div>
                  </div>
                )}

                {rightPanelTab === "preview" && (
                  <div>
                    <TabBar
                      tabs={networkOptions.map((n) => ({ key: n.value, label: n.label }))}
                      active={previewNetworkTab}
                      onChange={setPreviewNetworkTab}
                    />
                    <div className="mt-3">
                      {activeContent(previewNetworkTab) ? (
                        <SocialTabletPreview
                          content={activeContent(previewNetworkTab) ?? ""}
                          network={previewNetworkTab}
                        />
                      ) : (
                        <div className="flex flex-col items-center justify-center min-h-[160px] text-center text-xs text-muted-foreground/60 space-y-2">
                          <ImageIcon className="h-8 w-8 opacity-30" />
                          <p>Generate content in Convert tab to preview it here.</p>
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>
          ) : (
            <div className="flex flex-col items-center justify-center rounded-xl border border-border/30 border-dashed min-h-[300px] text-center p-8 space-y-3">
              <ScanSearch className="h-10 w-10 text-muted-foreground/30" />
              <p className="text-sm text-muted-foreground/60">
                Load a video and run analysis to unlock content generation.
              </p>
            </div>
          )}
        </div>
      </div>

      {/* History */}
      {historyItems.length > 0 && (
        <Card className="border-border/40 bg-background/60">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm flex items-center gap-2">
              <Clock className="h-4 w-4" />
              Recent Analyses
            </CardTitle>
          </CardHeader>
          <CardContent className="pt-0">
            <div className="divide-y divide-border/30">
              {historyItems.slice(0, 5).map((item) => (
                <div key={item.id} className="flex items-center gap-3 py-2.5">
                  <img
                    src={item.thumbnailUrl}
                    alt={item.title}
                    className="w-12 h-8 rounded object-cover shrink-0"
                  />
                  <div className="flex-1 min-w-0">
                    <p className="text-xs font-medium text-foreground/90 truncate">{item.title}</p>
                    <p className="text-xs text-muted-foreground/60 capitalize">
                      {item.networkSource} · {formatDate(item.analyzedAtUtc)}
                    </p>
                  </div>
                  {item.strategyMode && (
                    <Badge className={cn("text-xs capitalize shrink-0 border", strategyColors[item.strategyMode] ?? "")}>
                      {item.strategyMode}
                    </Badge>
                  )}
                  {item.downloadedAtUtc && (
                    <Badge variant="outline" className="text-xs shrink-0 border-green-500/30 text-green-600 dark:text-green-300">
                      Downloaded
                    </Badge>
                  )}
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
