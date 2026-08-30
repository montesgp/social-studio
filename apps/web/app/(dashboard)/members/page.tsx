import { UsersTable } from "@/components/admin/users-table";

export default function MembersPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Users / Members</h1>
        <p className="text-sm text-muted-foreground">
          Manage user accounts and their access status.
        </p>
      </div>
      <UsersTable />
    </div>
  );
}
