import {
  ContentWorkflowActionResult,
  ContentWorkflowHistory,
  ContentWorkflowHistoryItem,
  ContentWorkflowPackage,
  ContentWorkflowPreview,
  ContentWorkflowResult,
  ContentWorkflowSearchCriterion,
  ContentWorkflowSelection,
  ContentWorkflowSession,
  ContentWorkflowStatus,
  ContentWorkflowStep,
  ContentWorkflowStrategyMode,
  Organization,
  PageResult,
  SocialClient
} from "@/lib/api/types";

interface StartMockWorkflowInput {
  clientId: string;
  clientName: string;
  searchCriterion: ContentWorkflowSearchCriterion;
  strategyMode: ContentWorkflowStrategyMode;
  networkSources: string[];
  maxResults?: number;
}

interface MockWorkflowSessionState {
  workflowId: string;
  organizationId: string;
  clientId: string;
  clientName: string;
  searchCriterion: string;
  strategyMode: string;
  networkSources: string[];
  createdAtUtc: string;
  updatedAtUtc: string;
  results: ContentWorkflowResult[];
  selectedResultIds: string[];
  actions: ContentWorkflowActionResult[];
}

const mockOrganizations: Organization[] = [
  {
    id: "org-incoders-social",
    name: "Incoders Social",
    ownerUserId: "owner-01",
    isActive: true,
    deactivatedAtUtc: null,
    createdAtUtc: "2026-01-03T10:00:00.000Z",
    updatedAtUtc: "2026-05-10T12:00:00.000Z"
  }
];

const mockClientsByOrganization: Record<string, SocialClient[]> = {
  "org-incoders-social": [
    {
      id: "client-wellness-lab",
      organizationId: "org-incoders-social",
      name: "Wellness Lab",
      description: "Contenido diario de bienestar y hábitos saludables.",
      isActive: true,
      createdAtUtc: "2026-02-10T11:00:00.000Z",
      updatedAtUtc: "2026-05-01T09:30:00.000Z"
    },
    {
      id: "client-growth-studio",
      organizationId: "org-incoders-social",
      name: "Growth Studio",
      description: "Marca de marketing para founders y creadores.",
      isActive: true,
      createdAtUtc: "2026-03-15T15:00:00.000Z",
      updatedAtUtc: "2026-05-15T16:30:00.000Z"
    }
  ]
};

const networkCopy: Record<string, string> = {
  instagram: "Instagram",
  tiktok: "TikTok",
  youtube: "YouTube"
};

const mockSessions = new Map<string, MockWorkflowSessionState>();

const DEFAULT_WORKFLOW_ID = "wf-demo-wellness-lab-001";

function seedDefaultSession() {
  if (mockSessions.has(DEFAULT_WORKFLOW_ID)) {
    return;
  }

  const createdAtUtc = "2026-05-17T12:00:00.000Z";
  const results = buildMockResults(DEFAULT_WORKFLOW_ID, "viral", "mixed", ["tiktok", "instagram", "youtube"]);

  mockSessions.set(DEFAULT_WORKFLOW_ID, {
    workflowId: DEFAULT_WORKFLOW_ID,
    organizationId: "org-incoders-social",
    clientId: "client-wellness-lab",
    clientName: "Wellness Lab",
    searchCriterion: "viral",
    strategyMode: "mixed",
    networkSources: ["tiktok", "instagram", "youtube"],
    createdAtUtc,
    updatedAtUtc: createdAtUtc,
    results,
    selectedResultIds: [],
    actions: []
  });
}

export function getDefaultMockWorkflowId() {
  seedDefaultSession();
  return DEFAULT_WORKFLOW_ID;
}

export function getMockOrganizations() {
  seedDefaultSession();
  return mockOrganizations;
}

export function getMockClients(organizationId: string) {
  return mockClientsByOrganization[organizationId] ?? [];
}

export function startMockWorkflow(organizationId: string, input: StartMockWorkflowInput): ContentWorkflowSession {
  const now = new Date().toISOString();
  const workflowId = `wf-${Date.now()}-${Math.floor(Math.random() * 1000)}`;
  const results = buildMockResults(workflowId, input.searchCriterion, input.strategyMode, input.networkSources, input.maxResults);

  const session: MockWorkflowSessionState = {
    workflowId,
    organizationId,
    clientId: input.clientId,
    clientName: input.clientName,
    searchCriterion: input.searchCriterion,
    strategyMode: input.strategyMode,
    networkSources: input.networkSources,
    createdAtUtc: now,
    updatedAtUtc: now,
    results,
    selectedResultIds: [],
    actions: []
  };

  mockSessions.set(workflowId, session);

  return {
    workflowId,
    clientId: session.clientId,
    clientName: session.clientName,
    searchCriterion: session.searchCriterion,
    strategyMode: session.strategyMode,
    networkSources: session.networkSources,
    status: "in_progress",
    progressPercentage: 0,
    progressMessage: "Mock: retrieving videos from selected providers...",
    totalResults: session.results.length,
    selectedCount: 0,
    createdAtUtc: session.createdAtUtc,
    updatedAtUtc: session.updatedAtUtc
  };
}

export function getMockWorkflowStatus(organizationId: string, workflowId: string): ContentWorkflowStatus {
  const session = getOwnedSessionOrThrow(organizationId, workflowId);
  const createdAt = new Date(session.createdAtUtc).getTime();
  const elapsed = Math.max(0, (Date.now() - createdAt) / 1000);
  const progressPercentage = Math.min(100, Math.round(elapsed * 20));
  const isCompleted = progressPercentage >= 100;

  const phases: Array<{ key: string; label: string; message: string }> = [
    ...session.networkSources.map((network) => ({
      key: `fetch-${network}`,
      label: `Fetch ${networkCopy[network] ?? network}`,
      message: `Mock: loading videos from ${networkCopy[network] ?? network} source...`
    })),
    {
      key: "analysis",
      label: "Analysis",
      message: "Mock: building video analysis and script details..."
    },
    {
      key: "ranking",
      label: "Ranking & cards",
      message: "Mock: assembling ranked cards and pagination..."
    }
  ];

  const activeIndex = isCompleted ? phases.length - 1 : Math.min(phases.length - 1, Math.floor(elapsed / 1.6));
  const steps: ContentWorkflowStep[] = phases.map((phase, index) => {
    if (isCompleted || index < activeIndex) {
      return { key: phase.key, label: phase.label, status: "completed", message: phase.message, progressPercentage: 100 };
    }

    if (index === activeIndex) {
      return { key: phase.key, label: phase.label, status: "in_progress", message: phase.message, progressPercentage: 65 };
    }

    return { key: phase.key, label: phase.label, status: "pending", message: phase.message, progressPercentage: 0 };
  });

  return {
    workflowId: session.workflowId,
    status: isCompleted ? "completed" : "in_progress",
    progressPercentage,
    progressMessage: isCompleted ? "Mock generation completed." : phases[activeIndex]?.message ?? "Mock generation running...",
    steps,
    updatedAtUtc: new Date().toISOString(),
    totalResults: session.results.length
  };
}

export function getMockWorkflowResults(
  organizationId: string,
  workflowId: string,
  page: number,
  pageSize: number
): PageResult<ContentWorkflowResult> {
  const session = getOwnedSessionOrThrow(organizationId, workflowId);
  const safePage = Math.max(1, page);
  const safePageSize = Math.max(1, pageSize);
  const start = (safePage - 1) * safePageSize;
  const items = session.results.slice(start, start + safePageSize).map((item) => ({
    ...item,
    isSelected: session.selectedResultIds.includes(item.id)
  }));

  return {
    items,
    page: safePage,
    pageSize: safePageSize,
    totalCount: session.results.length,
    totalPages: Math.max(1, Math.ceil(session.results.length / safePageSize)),
    hasPreviousPage: safePage > 1,
    hasNextPage: start + safePageSize < session.results.length
  };
}

export function getMockWorkflowPreview(
  organizationId: string,
  workflowId: string,
  resultId: string
): ContentWorkflowPreview {
  const session = getOwnedSessionOrThrow(organizationId, workflowId);
  const result = session.results.find((item) => item.id === resultId);

  if (!result) {
    throw { message: "Mock preview not found for selected result." };
  }

  return {
    workflowId,
    resultId: result.id,
    resultTitle: result.title,
    videoAnalysis: [
      `Network source: ${networkCopy[result.networkSource] ?? result.networkSource}`,
      `Hook style: ${result.searchCriterion} opener with direct promise in first seconds.`,
      `Format angle: ${result.strategyMode} narrative with visual rhythm and CTA pacing.`,
      `Signals: ${result.views.toLocaleString()} views · ${result.likes.toLocaleString()} likes · ${result.comments.toLocaleString()} comments.`
    ].join("\n"),
    scriptDraft: [
      "1. Hook (0-3s): open with the strongest transformation.",
      "2. Context (3-8s): why this matters for the audience now.",
      "3. Main body (8-28s): 3 tactical points with visual cue each.",
      "4. CTA (last 4s): save/share/comment with one clear action."
    ].join("\n")
  };
}

export function selectMockWorkflowResults(
  organizationId: string,
  workflowId: string,
  resultIds: string[]
): ContentWorkflowSelection {
  const session = getOwnedSessionOrThrow(organizationId, workflowId);
  const knownIds = new Set(session.results.map((item) => item.id));
  const selected = Array.from(new Set(resultIds)).filter((id) => knownIds.has(id));
  session.selectedResultIds = selected;
  session.updatedAtUtc = new Date().toISOString();

  return {
    workflowId,
    selectedCount: selected.length,
    selectedResultIds: selected,
    updatedAtUtc: session.updatedAtUtc
  };
}

export function runMockWorkflowAction(
  organizationId: string,
  workflowId: string,
  action: string,
  resultIds: string[]
): ContentWorkflowActionResult {
  const session = getOwnedSessionOrThrow(organizationId, workflowId);
  const selected = Array.from(new Set(resultIds));
  const executedAtUtc = new Date().toISOString();
  const response: ContentWorkflowActionResult = {
    actionId: `act-${Date.now()}-${Math.floor(Math.random() * 1000)}`,
    workflowId,
    action,
    status: "completed",
    resultIds: selected,
    message: `Mock action '${action}' completed for ${selected.length} item(s).`,
    executedAtUtc
  };

  session.actions = [response, ...session.actions];
  session.updatedAtUtc = executedAtUtc;

  return response;
}

export function generateMockWorkflowPackage(organizationId: string, workflowId: string): ContentWorkflowPackage {
  const session = getOwnedSessionOrThrow(organizationId, workflowId);
  if (session.selectedResultIds.length === 0) {
    throw { message: "Select at least one content item before generating package." };
  }

  const generatedAtUtc = new Date().toISOString();
  const packageFileName = `mock-monthly-content-${workflowId}.zip`;

  session.updatedAtUtc = generatedAtUtc;
  session.actions = [
    {
      actionId: `act-${Date.now()}-${Math.floor(Math.random() * 1000)}`,
      workflowId,
      action: "generate_package",
      status: "completed",
      resultIds: [...session.selectedResultIds],
      message: `Mock package built with ${session.selectedResultIds.length} selected content items.`,
      executedAtUtc: generatedAtUtc
    },
    ...session.actions
  ];

  return {
    workflowId,
    packageFileName,
    downloadUrl: `/mock-downloads/${packageFileName}`,
    status: "simulated_ready",
    itemCount: session.selectedResultIds.length,
    generatedAtUtc
  };
}

export function getMockWorkflowHistory(organizationId: string, workflowId: string): ContentWorkflowHistory {
  const session = getOwnedSessionOrThrow(organizationId, workflowId);
  return {
    summary: toHistoryItem(session),
    actions: [...session.actions]
  };
}

export function listMockWorkflowHistory(organizationId: string, limit = 12): ContentWorkflowHistoryItem[] {
  return Array.from(mockSessions.values())
    .filter((session) => session.organizationId === organizationId)
    .sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime())
    .slice(0, Math.max(1, limit))
    .map(toHistoryItem);
}

function getOwnedSessionOrThrow(organizationId: string, workflowId: string) {
  const session = mockSessions.get(workflowId);
  if (!session || session.organizationId !== organizationId) {
    throw { message: "Mock workflow not found for current organization." };
  }

  return session;
}

function toHistoryItem(session: MockWorkflowSessionState): ContentWorkflowHistoryItem {
  return {
    workflowId: session.workflowId,
    clientId: session.clientId,
    clientName: session.clientName,
    searchCriterion: session.searchCriterion,
    strategyMode: session.strategyMode,
    networkSources: session.networkSources,
    status: "completed",
    totalResults: session.results.length,
    selectedCount: session.selectedResultIds.length,
    createdAtUtc: session.createdAtUtc,
    updatedAtUtc: session.updatedAtUtc
  };
}

function buildMockResults(
  workflowId: string,
  searchCriterion: string,
  strategyMode: string,
  networkSources: string[],
  maxResults?: number
): ContentWorkflowResult[] {
  const networks = networkSources.length > 0 ? networkSources : ["instagram"];
  const result: ContentWorkflowResult[] = [];
  const concreteStrategies: ContentWorkflowStrategyMode[] = ["educational", "informative", "entertainment"];

  const hooks = [
    "3 mistakes creators repeat every week",
    "The framework behind high-retention hooks",
    "How to turn one idea into 12 content pieces",
    "This post format doubled saves in 7 days",
    "The social media ritual that scales output"
  ];

  for (const network of networks) {
    for (let i = 0; i < 14; i++) {
      const hook = hooks[i % hooks.length];
      const views = 12000 + i * 3100 + (network === "tiktok" ? 9000 : network === "youtube" ? 7000 : 5000);
      const likes = Math.round(views * 0.078);
      const comments = Math.round(likes * 0.14);
      const id = `${workflowId}-${network}-${i + 1}`;
      const resolvedStrategy = strategyMode === "mixed" ? concreteStrategies[i % 3]! : strategyMode;

      result.push({
        id,
        title: `${networkCopy[network] ?? network}: ${hook}`,
        networkSource: network,
        searchCriterion,
        strategyMode: resolvedStrategy,
        videoUrl: `https://example.com/mock/${network}/video-${i + 1}`,
        thumbnailUrl: `https://picsum.photos/seed/${encodeURIComponent(id)}/640/360`,
        summary: `Mock detail for ${networkCopy[network] ?? network} candidate #${i + 1}. Focus: ${resolvedStrategy} content using ${searchCriterion} search logic.`,
        views,
        likes,
        comments,
        isSelected: false
      });
    }
  }

  const sorted = result.sort((a, b) => b.views - a.views);
  return maxResults !== undefined ? sorted.slice(0, maxResults) : sorted;
}
