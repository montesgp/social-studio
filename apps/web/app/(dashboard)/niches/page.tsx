import { NichesAdminClient } from "@/components/social/niches/niches-admin-client";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { redirect } from "next/navigation";

export default async function NichesPage() {
  const authSession = await getServerAuthSession();
  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }
  return <NichesAdminClient isPlatformAdmin={authSession.isPlatformAdmin} />;
}
