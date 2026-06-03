import { AnalyzerModeSwitch } from "@/components/social/AnalyzerModeSwitch";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { redirect } from "next/navigation";

export default async function AnalyzerPage() {
  const authSession = await getServerAuthSession();
  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }
  return <AnalyzerModeSwitch isPlatformAdmin={authSession.isPlatformAdmin} />;
}
