import { OverviewCards } from "@/components/dashboard/overview-cards";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export default function DashboardPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Hive Social Dashboard</h1>
        <p className="text-sm text-muted-foreground">
          Incoders Hive platform · Hive Social module bootstrap.
        </p>
      </div>

      <OverviewCards />

      <Card>
        <CardHeader>
          <CardTitle>Current MVP Scope</CardTitle>
        </CardHeader>
        <CardContent className="text-sm text-muted-foreground">
          Organizations + workflow placeholders are enabled. Campaigns, Leads, Analytics, Automations and
          Agents are intentionally out of this iteration.
        </CardContent>
      </Card>
    </div>
  );
}
