# Multi Tenancy Spec

## Purpose
Define organization/tenant capabilities, constraints, invariants, events, and acceptance criteria.

## Capabilities
- Create an organization for the current authenticated user.
- List, search and paginate organizations.
- Read organization details by id.
- Rename/update an organization.
- Archive/deactivate an organization for normal recoverable lifecycle management.
- Reactivate an archived organization.
- Temporarily hard-delete an organization during MVP cleanup when a platform sysadmin confirms the destructive operation.
- Use organization id as the root boundary for Social module data.
- Configure workspace module visibility per organization-user association, including explicit module ordering.
- Sysadmin user administration MUST support filtering users by organization assignment and show the concrete organization-to-module mapping instead of aggregate-only counts.

## Events
- OrganizationCreated
- OrganizationRenamed
- OrganizationArchived
- OrganizationReactivated
- OrganizationHardDeleted

## Acceptance Criteria
- A user can create an organization from the web cockpit.
- A user can list, search, inspect, update, archive, reactivate and, when authorized as platform sysadmin, hard-delete organizations from the web cockpit.
- The web cockpit shows either grid view or table view according to the user's current selection; grid is the default.
- Grid view and table view share the same organization pagination behavior.
- The web cockpit surfaces operation errors and success states through in-app notifications.
- Archived organizations are recoverable.
- Organization hard-delete is a temporary MVP cleanup exception, not the normal lifecycle path.
- Organization hard-delete requires the existing confirmation modal and permanently removes organization-scoped users, memberships, user module assignments, system settings, integrations/credentials/tokens, clients, client child chains, analyses, usage events, generated packages and stored assets.
- Organization hard-delete MUST preserve platform sysadmin user accounts even if stale organization membership data exists.
- Organization-owned module data must be scoped by `organization_id`.
- Sysadmins can assign and reorder modules per user and organization; non-sysadmin users only see assigned modules in the workspace selector for the active organization.
- Sysadmins can filter the users table by organization and inspect which modules each listed user has for each organization.
