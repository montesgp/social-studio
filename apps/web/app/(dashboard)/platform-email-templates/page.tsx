import { redirect } from "next/navigation";
import { EmailTemplatesClient } from "@/components/platform/email-templates-client";
import { getServerAuthSession } from "@/lib/auth/server-session";

export default async function PlatformEmailTemplatesPage() {
  const session = await getServerAuthSession();
  if (!session.accessToken || session.isExpired) {
    redirect("/login");
  }

  if (!session.isPlatformAdmin) {
    redirect("/dashboard");
  }

  return <EmailTemplatesClient />;
}
