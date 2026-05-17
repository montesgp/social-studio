import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";

interface AuthCardProps {
  title: string;
  description?: string;
  children: React.ReactNode;
}

export function AuthCard({ title, description, children }: AuthCardProps) {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="w-full max-w-md space-y-3">
        <div className="text-center">
          <p className="text-xs uppercase tracking-widest text-muted-foreground">Incoders Hive</p>
          <h1 className="text-2xl font-semibold">Hive Social</h1>
        </div>
        <Card className="w-full">
          <CardHeader>
            <CardTitle>{title}</CardTitle>
            {description ? <CardDescription>{description}</CardDescription> : null}
          </CardHeader>
          <CardContent>{children}</CardContent>
        </Card>
      </div>
    </main>
  );
}
