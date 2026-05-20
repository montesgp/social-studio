import { NetworksAdminClient } from "@/components/social/networks/networks-admin-client";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { redirect } from "next/navigation";

export default async function SocialNetworksPage() {
  const authSession = await getServerAuthSession();
  if (!authSession.accessToken || authSession.isExpired) {
    redirect("/login");
  }
  return <NetworksAdminClient isPlatformAdmin={authSession.isPlatformAdmin} />;
}
