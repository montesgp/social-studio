import type { Metadata } from "next";
import "./globals.css";
import { ToastProvider } from "@/components/ui/toaster";

export const metadata: Metadata = {
  title: "Incoders Hive",
  description: "AI-native workflow platform."
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
