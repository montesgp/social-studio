import { redirect } from "next/navigation";
import { LoginForm } from "@/components/auth/login-form";
import { AuthCard } from "@/components/auth/auth-card";
import { getServerAuthSession } from "@/lib/auth/server-session";
import { getCurrentUser } from "@/lib/api/me";

export default async function LoginPage() {
  const authSession = await getServerAuthSession();

  if (authSession.accessToken && !authSession.isExpired) {
    try {
      await getCurrentUser(authSession.accessToken);
      redirect("/dashboard");
    } catch {
      // If token is stale we keep user on login page.
    }
  }

  return (
    <AuthCard title="Sign in to Hive Social">
      <LoginForm />
    </AuthCard>
  );
}
