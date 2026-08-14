"use client";

/**
 * AI ANALYSIS — the discovery candidate bank beside a chat that can interrogate it.
 *
 * Layout is the argument. The bank's rows are on the main area and the chat is next to them, so every
 * claim the assistant makes can be checked against the same table without leaving the page. The chat
 * shows WHICH query answered each question and cites the candidates it used, because an unverifiable
 * ranking is worthless for deciding where to spend generation budget.
 *
 * This component is deliberately thin: what may be asked, how an answer is restated, how citations are
 * summarised and how numbers render all live in `lib/social/discovery-bank-intelligence.ts`, under test.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BrainCircuit, ExternalLink, Loader2, Search, Send } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useAccessToken } from "@/components/auth/use-access-token";
import { useDateFormatter } from "@/lib/format/use-date-formatter";
import {
  askDiscoveryBank,
  listDiscoveryBank,
  type DiscoveryBankCandidate,
} from "@/lib/api/discovery-bank";
import {
  BANK_EMPTY_HEADLINE,
  COMPUTED_NOT_GUESSED_NOTE,
  buildChatHistory,
  formatMetricValue,
  isAskable,
  isBankRequestReady,
  summariseCitations,
  toChatMessage,
  type ChatMessage,
  type DiscoveryBankSupportedQuery,
} from "@/lib/social/discovery-bank-intelligence";

const PAGE_SIZE = 50;

export function AiAnalysisClient() {
  const { formatDate } = useDateFormatter();
  const token = useAccessToken();
  const isReady = isBankRequestReady(token);

  const [candidates, setCandidates] = useState<DiscoveryBankCandidate[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [isLoadingBank, setIsLoadingBank] = useState(true);
  const [bankError, setBankError] = useState<string | null>(null);
  const [supportedQueries, setSupportedQueries] = useState<DiscoveryBankSupportedQuery[]>([]);

  const [question, setQuestion] = useState("");
  const [isAsking, setIsAsking] = useState(false);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [askError, setAskError] = useState<string | null>(null);

  const messageIdRef = useRef(0);
  const nextMessageId = useCallback(() => {
    messageIdRef.current += 1;
    return `m${messageIdRef.current}`;
  }, []);

  useEffect(() => {
    // The token resolves one tick after mount. Staying in the loading state until it does keeps the
    // page from reporting a 401 race as "the bank could not be loaded".
    if (!isReady) return;

    let cancelled = false;

    setIsLoadingBank(true);
    setBankError(null);

    listDiscoveryBank(page, PAGE_SIZE, token)
      .then((result) => {
        if (cancelled) return;
        setCandidates(result.items);
        setTotalCount(result.totalCount);
        setSupportedQueries(result.supportedQueries);
      })
      .catch(() => {
        if (cancelled) return;
        setBankError("The discovery candidate bank could not be loaded. Try again in a moment.");
      })
      .finally(() => {
        if (!cancelled) setIsLoadingBank(false);
      });

    return () => {
      cancelled = true;
    };
  }, [page, isReady, token]);

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const isBankEmpty = !isLoadingBank && totalCount === 0;

  const canAsk = useMemo(() => isReady && isAskable(question, isAsking), [isReady, question, isAsking]);

  const handleAsk = useCallback(async () => {
    if (!isReady || !isAskable(question, isAsking)) return;

    const asked = question.trim();
    const history = buildChatHistory(messages);

    setMessages((current) => [
      ...current,
      {
        id: nextMessageId(),
        role: "user",
        text: asked,
        tone: "normal",
        queryLabel: null,
        citations: [],
        groups: [],
        matchedCount: 0,
        supportedQueries: [],
      },
    ]);
    setQuestion("");
    setIsAsking(true);
    setAskError(null);

    try {
      const answer = await askDiscoveryBank(asked, history, token);
      setMessages((current) => [...current, toChatMessage(answer, nextMessageId())]);
    } catch {
      setAskError("The question could not be answered right now. Check the AI provider configured for Single Analyzer.");
    } finally {
      setIsAsking(false);
    }
  }, [isReady, token, question, isAsking, messages, nextMessageId]);

  return (
    <div className="flex flex-col gap-6 p-6">
      <header className="flex flex-col gap-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <BrainCircuit className="h-6 w-6 text-primary" aria-hidden />
          AI Analysis
        </h1>
        <p className="max-w-3xl text-sm text-muted-foreground">{COMPUTED_NOT_GUESSED_NOTE}</p>
      </header>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
        {/* ── the bank ─────────────────────────────────────────────────── */}
        <Card className="min-w-0">
          <CardHeader>
            <CardTitle>Discovery candidate bank</CardTitle>
            <CardDescription>
              {isLoadingBank
                ? "Loading…"
                : `${totalCount.toLocaleString("en-US")} candidate${totalCount === 1 ? "" : "s"} banked for this organization.`}
            </CardDescription>
          </CardHeader>
          <CardContent className="min-w-0">
            {bankError ? <p className="text-sm text-destructive">{bankError}</p> : null}

            {isBankEmpty && !bankError ? (
              <div className="flex flex-col items-start gap-2 rounded-lg border border-dashed p-6">
                <Search className="h-5 w-5 text-muted-foreground" aria-hidden />
                <p className="text-sm font-medium">No discovery data yet</p>
                <p className="text-sm text-muted-foreground">{BANK_EMPTY_HEADLINE}</p>
              </div>
            ) : null}

            {!isBankEmpty && !bankError ? (
              <div className="min-w-0 overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Candidate</TableHead>
                      <TableHead>Network</TableHead>
                      <TableHead className="text-right">Views</TableHead>
                      <TableHead className="text-right">Likes</TableHead>
                      <TableHead className="text-right">Comments</TableHead>
                      <TableHead className="text-right">Shares</TableHead>
                      <TableHead className="text-right">Saves</TableHead>
                      <TableHead>Published</TableHead>
                      <TableHead>Hashtags</TableHead>
                      <TableHead>Video</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {candidates.map((candidate) => (
                      <TableRow key={candidate.candidateKey}>
                        <TableCell className="max-w-[280px]">
                          <div className="font-medium">{candidate.authorHandle}</div>
                          <div className="truncate text-xs text-muted-foreground">{candidate.title}</div>
                        </TableCell>
                        <TableCell>
                          <Badge variant="muted">{candidate.networkKey}</Badge>
                        </TableCell>
                        <TableCell className="text-right tabular-nums">{formatMetricValue(candidate.views)}</TableCell>
                        <TableCell className="text-right tabular-nums">{formatMetricValue(candidate.likes)}</TableCell>
                        <TableCell className="text-right tabular-nums">{formatMetricValue(candidate.comments)}</TableCell>
                        <TableCell className="text-right tabular-nums">{formatMetricValue(candidate.shares)}</TableCell>
                        <TableCell className="text-right tabular-nums">{formatMetricValue(candidate.saves)}</TableCell>
                        <TableCell className="whitespace-nowrap text-sm">{formatDate(candidate.publishedAtUtc)}</TableCell>
                        <TableCell className="max-w-[180px]">
                          <div className="flex flex-wrap gap-1">
                            {candidate.hashtags.length === 0 ? (
                              <span className="text-xs text-muted-foreground">—</span>
                            ) : (
                              candidate.hashtags.map((tag) => (
                                <Badge key={tag} variant="outline">
                                  #{tag}
                                </Badge>
                              ))
                            )}
                          </div>
                        </TableCell>
                        <TableCell>
                          <a
                            className="inline-flex items-center gap-1 text-sm text-primary hover:underline"
                            href={candidate.contentUrl}
                            target="_blank"
                            rel="noreferrer noopener"
                          >
                            Open
                            <ExternalLink className="h-3 w-3" aria-hidden />
                          </a>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>

                {totalPages > 1 ? (
                  <div className="flex items-center justify-between pt-4">
                    <span className="text-sm text-muted-foreground">
                      Page {page} of {totalPages}
                    </span>
                    <div className="flex gap-2">
                      <Button variant="outline" disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))}>
                        Previous
                      </Button>
                      <Button
                        variant="outline"
                        disabled={page >= totalPages}
                        onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                      >
                        Next
                      </Button>
                    </div>
                  </div>
                ) : null}
              </div>
            ) : null}
          </CardContent>
        </Card>

        {/* ── the chat ─────────────────────────────────────────────────── */}
        <Card className="flex min-w-0 flex-col">
          <CardHeader>
            <CardTitle>Ask the bank</CardTitle>
            <CardDescription>
              Every answer names the query that produced it and cites the candidates it used.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex min-h-0 flex-1 flex-col gap-4">
            <div className="flex max-h-[520px] min-h-[220px] flex-1 flex-col gap-3 overflow-y-auto">
              {messages.length === 0 ? (
                <div className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
                  <p className="mb-2 font-medium text-foreground">What this tool can answer</p>
                  <ul className="list-disc space-y-1 pl-4">
                    {supportedQueries.map((entry) => (
                      <li key={entry.key}>
                        <span className="text-foreground">{entry.label}</span> — e.g. &ldquo;{entry.exampleQuestion}&rdquo;
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}

              {messages.map((message) => (
                <ChatBubble key={message.id} message={message} />
              ))}

              {isAsking ? (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
                  Running the query…
                </div>
              ) : null}
            </div>

            {askError ? <p className="text-sm text-destructive">{askError}</p> : null}

            <form
              className="flex gap-2"
              onSubmit={(event) => {
                event.preventDefault();
                void handleAsk();
              }}
            >
              <Input
                value={question}
                onChange={(event) => setQuestion(event.target.value)}
                placeholder="Top 5 by views"
                aria-label="Ask a question about the discovery candidate bank"
              />
              <Button type="submit" disabled={!canAsk}>
                <Send className="h-4 w-4" aria-hidden />
                <span className="sr-only">Ask</span>
              </Button>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function ChatBubble({ message }: { message: ChatMessage }) {
  const { formatDate } = useDateFormatter();

  if (message.role === "user") {
    return (
      <div className="self-end rounded-lg bg-primary/10 px-3 py-2 text-sm">{message.text}</div>
    );
  }

  const citationSummary = summariseCitations(message.citations.length, message.matchedCount);

  return (
    <div className="flex flex-col gap-2 rounded-lg border bg-muted/30 p-3">
      {message.queryLabel ? (
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{message.queryLabel}</p>
      ) : null}

      <p className="text-sm">{message.text}</p>

      {message.groups.length > 0 ? (
        <ul className="space-y-1 text-xs">
          {message.groups.map((group) => (
            <li key={group.key} className="flex justify-between gap-3">
              <span className="font-medium">{group.key}</span>
              <span className="tabular-nums text-muted-foreground">
                {group.candidateCount} · {formatMetricValue(group.metricTotal)}
              </span>
            </li>
          ))}
        </ul>
      ) : null}

      {message.citations.length > 0 ? (
        <div className="flex flex-col gap-2">
          <p className="text-xs text-muted-foreground">{citationSummary}</p>
          <ol className="space-y-1 text-xs">
            {message.citations.map((citation) => (
              <li key={citation.candidateKey} className="flex flex-col">
                <a
                  className="inline-flex items-center gap-1 font-medium text-primary hover:underline"
                  href={citation.contentUrl}
                  target="_blank"
                  rel="noreferrer noopener"
                >
                  {citation.authorHandle}
                  <ExternalLink className="h-3 w-3" aria-hidden />
                </a>
                <span className="text-muted-foreground">
                  {formatMetricValue(citation.metricValue)} · {citation.networkKey} ·{" "}
                  {formatDate(citation.publishedAtUtc)}
                </span>
              </li>
            ))}
          </ol>
        </div>
      ) : null}

      {message.tone === "refused" && message.supportedQueries.length > 0 ? (
        <ul className="list-disc space-y-1 pl-4 text-xs text-muted-foreground">
          {message.supportedQueries.map((entry) => (
            <li key={entry.key}>
              <span className="text-foreground">{entry.label}</span> — e.g. &ldquo;{entry.exampleQuestion}&rdquo;
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
