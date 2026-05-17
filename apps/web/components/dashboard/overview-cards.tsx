import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

const cards = [
  { title: "Organizations", value: "Ready", description: "CRUD + archive endpoints and UI connected." },
  { title: "Members", value: "Next", description: "Membership and role workflows pending." },
  { title: "Content Pipeline", value: "Planned", description: "Research, ideas and publications placeholders." }
];

export function OverviewCards() {
  return (
    <div className="grid gap-4 md:grid-cols-3">
      {cards.map((card) => (
        <Card key={card.title}>
          <CardHeader>
            <CardTitle className="text-base">{card.title}</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-xl font-semibold">{card.value}</p>
            <p className="text-sm text-muted-foreground">{card.description}</p>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
