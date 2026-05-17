#!/usr/bin/env node
// One-time script to create auth pages that require new directories.
// Run: node scripts/create-auth-pages.js (from repo root)
const fs = require("fs");
const path = require("path");

const webApp = path.join(__dirname, "..", "apps", "web", "app");

const files = {
  [path.join(webApp, "auth", "callback", "route.ts")]: `import { NextResponse } from "next/server";
import { createClient } from "@/utils/supabase/server";

export async function GET(request: Request) {
  const { searchParams, origin } = new URL(request.url);
  const code = searchParams.get("code");
  const next = searchParams.get("next") ?? "/dashboard";

  if (code) {
    const supabase = await createClient();
    const { error } = await supabase.auth.exchangeCodeForSession(code);
    if (!error) {
      return NextResponse.redirect(\`\${origin}\${next}\`);
    }
  }

  return NextResponse.redirect(\`\${origin}/login?error=auth_callback_failed\`);
}
`,

  [path.join(webApp, "forgot-password", "page.tsx")]: `import { AuthCard } from "@/components/auth/auth-card";
import { ForgotPasswordForm } from "@/components/auth/forgot-password-form";

export default function ForgotPasswordPage() {
  return (
    <AuthCard
      title="Reset your password"
      description="Enter your email and we'll send you a reset link."
    >
      <ForgotPasswordForm />
    </AuthCard>
  );
}
`,

  [path.join(webApp, "reset-password", "page.tsx")]: `import { AuthCard } from "@/components/auth/auth-card";
import { ResetPasswordForm } from "@/components/auth/reset-password-form";

export default function ResetPasswordPage() {
  return (
    <AuthCard title="Set new password">
      <ResetPasswordForm />
    </AuthCard>
  );
}
`,
};

for (const [filePath, content] of Object.entries(files)) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, content, "utf8");
  console.log("Created:", filePath);
}

console.log("\nDone! You can now delete this script: scripts/create-auth-pages.js");
