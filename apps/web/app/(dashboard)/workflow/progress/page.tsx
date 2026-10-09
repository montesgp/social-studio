import { WorkflowProgressLandingClient } from "@/components/social/workflow/progress/workflow-progress-landing-client";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { redirect } from "next/navigation";

/**
 * Free-standing landing page for enqueued work — reachable at any time from the Generator header, not
 * only right after enqueueing a specific workflow. See `WorkflowProgressLandingClient`.
 */
export default async function WorkflowProgressLandingPage() {
  const authSession = await getServerAuthSession();
  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }
  return <WorkflowProgressLandingClient />;
}
