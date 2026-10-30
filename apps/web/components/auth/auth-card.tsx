import { HiveBackdrop } from "@/components/auth/hive-backdrop";
import { HiveLogo } from "@/components/auth/studio-logo";
import { HiveModuleComb } from "@/components/auth/hive-module-comb";

interface AuthCardProps {
  title: string;
  description?: string;
  children: React.ReactNode;
}

/**
 * The front door of Social Studio.
 *
 * The comb is the product metaphor, so the entry screen shows it literally: a field of cells behind
 * a floating glass card. On narrow viewports the module cluster steps aside — the form is what a
 * phone user came for.
 */
export function AuthCard({ title, description, children }: AuthCardProps) {
  return (
    <main className="relative flex min-h-screen items-center justify-center overflow-hidden bg-background px-4 py-10">
      <HiveBackdrop className="pointer-events-none absolute inset-0" />

      <div className="relative z-10 flex w-full max-w-5xl flex-col items-center gap-12 lg:flex-row lg:items-center lg:justify-between lg:gap-8">
        <HiveModuleComb className="hidden lg:block" />

        <div className="w-full max-w-md">
          <div className="rounded-2xl border border-border/70 bg-card/70 p-6 shadow-2xl shadow-black/20 backdrop-blur-xl sm:p-8">
            <div className="mb-8 flex items-center justify-center gap-3">
              <HiveLogo className="h-11 w-11 text-foreground" />
              <div className="leading-none">
                <p className="text-[11px] font-medium uppercase tracking-[0.32em] text-muted-foreground">
                  Incoders
                </p>
                <p className="mt-1 text-2xl font-semibold tracking-tight">HIVE</p>
              </div>
            </div>

            <div className="mb-5">
              <h1 className="text-lg font-semibold">{title}</h1>
              {description ? (
                <p className="mt-1 text-sm text-muted-foreground">{description}</p>
              ) : null}
            </div>

            {children}
          </div>

          <p className="mt-6 text-center text-[11px] text-muted-foreground">
            incoders.com.ar — AI-native operations, one cell at a time.
          </p>
        </div>
      </div>
    </main>
  );
}
