// Client for the AI Analysis tool — the discovery candidate bank plus the question endpoint.
//
// Thin on purpose: every decision about what the screen may say lives in
// `lib/social/discovery-bank-intelligence.ts`, where it is tested.

import { apiRequest } from "@/lib/api/client";
import type {
  DiscoveryBankAnswer,
  DiscoveryBankCitation,
  DiscoveryBankSupportedQuery,
} from "@/lib/social/discovery-bank-intelligence";

export type { DiscoveryBankAnswer, DiscoveryBankCitation, DiscoveryBankSupportedQuery };

/** One banked candidate as the table renders it. */
export interface DiscoveryBankCandidate {
  candidateKey: string;
  networkKey: string;
  contentUrl: string;
  authorHandle: string;
  title: string;
  hashtags: string[];
  publishedAtUtc: string | null;
  observedAtUtc: string;
  views: number | null;
  likes: number | null;
  comments: number | null;
  shares: number | null;
  saves: number | null;
  opportunityScore: number | null;
}

export interface DiscoveryBankPage {
  items: DiscoveryBankCandidate[];
  totalCount: number;
  page: number;
  pageSize: number;
  supportedQueries: DiscoveryBankSupportedQuery[];
}

// Both endpoints are `RequireAuthorization()`, so the caller MUST hand over the session token: without
// it the request is a 401 the page can only render as a loading failure. The tenant header is added by
// `apiRequest` from the active organization.
export function listDiscoveryBank(
  page: number,
  pageSize: number,
  token?: string
): Promise<DiscoveryBankPage> {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  return apiRequest<DiscoveryBankPage>(
    `/api/social/discovery-bank/?${query.toString()}`,
    undefined,
    token
  );
}

export function askDiscoveryBank(
  question: string,
  history: { role: "user" | "assistant"; text: string }[],
  token?: string
): Promise<DiscoveryBankAnswer> {
  return apiRequest<DiscoveryBankAnswer>(
    "/api/social/discovery-bank/ask",
    {
      method: "POST",
      body: JSON.stringify({ question, history }),
    },
    token
  );
}
