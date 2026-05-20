import { WorkflowClient } from "@/components/social/workflow/workflow-client";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { redirect } from "next/navigation";

export default async function WorkflowPage() {
  const authSession = await getServerAuthSession();
  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }
  return <WorkflowClient isPlatformAdmin={authSession.isPlatformAdmin} />;
}
