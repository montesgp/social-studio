import { redirect } from "next/navigation";
import { AiAnalysisClient } from "@/components/social/discovery/ai-analysis-client";
import { getServerAuthSession } from "@/lib/auth/server-session";

export default async function AiAnalysisPage() {
  const authSession = await getServerAuthSession();
  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }

  return <AiAnalysisClient />;
}
