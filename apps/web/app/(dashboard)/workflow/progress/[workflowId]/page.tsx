import { WorkflowProgressClient } from "@/components/social/workflow/progress/workflow-progress-client";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { redirect } from "next/navigation";

interface WorkflowProgressPageProps {
  params: Promise<{ workflowId: string }>;
}

/**
 * Operator-owned progress view for one enqueued workflow (see EnqueueSummary's post-enqueue link).
 * Reachable by any authenticated org user — no `isPlatformAdmin` gate — mirroring the plain auth-only
 * pattern from `/workflow`, NOT the admin-gated `/content-generation-jobs` screen this replaces.
 */
export default async function WorkflowProgressPage({ params }: WorkflowProgressPageProps) {
  const { workflowId } = await params;
  const authSession = await getServerAuthSession();
  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }
  return <WorkflowProgressClient workflowId={workflowId} />;
}
