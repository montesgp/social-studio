import type { Metadata } from "next";
import "./globals.css";
import { ToastProvider } from "@/components/ui/toaster";

export const metadata: Metadata = {
  title: "Incoders Hive Social",
  description: "AI-native workflow dashboard for social content operations."
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" data-theme="dark" suppressHydrationWarning>
      <body>
        <ToastProvider>{children}</ToastProvider>
      </body>
    </html>
  );
}
