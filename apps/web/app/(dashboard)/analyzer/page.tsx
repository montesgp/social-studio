import { SingleAnalyzerClient } from "@/components/social/single-analyzer-client";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { redirect } from "next/navigation";

export default async function AnalyzerPage() {
  const authSession = await getServerAuthSession();
  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }
  return <SingleAnalyzerClient isPlatformAdmin={authSession.isPlatformAdmin} />;
}
