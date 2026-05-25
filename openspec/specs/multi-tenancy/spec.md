# Multi Tenancy Spec

## Purpose
Define organization/tenant capabilities, constraints, invariants, events, and acceptance criteria.

## Capabilities
- Create an organization for the current authenticated user.
- List, search and paginate organizations.
- Read organization details by id.
- Rename/update an organization.
- Archive/deactivate an organization instead of hard-deleting it.
- Reactivate an archived organization.
- Use organization id as the root boundary for Social module data.
- Configure workspace module visibility per organization-user association, including explicit module ordering.

## Events
- OrganizationCreated
- OrganizationRenamed
- OrganizationArchived
- OrganizationReactivated

## Acceptance Criteria
- A user can create an organization from the web cockpit.
- A user can list, search, inspect, update, archive and reactivate organizations from the web cockpit.
- The web cockpit shows either grid view or table view according to the user's current selection; grid is the default.
- Grid view and table view share the same organization pagination behavior.
- The web cockpit surfaces operation errors and success states through in-app notifications.
- Archived organizations are recoverable.
- Hard-delete is not part of the MVP organization lifecycle.
- Organization-owned module data must be scoped by `organization_id`.
- Sysadmins can assign and reorder modules per user and organization; non-sysadmin users only see assigned modules in the workspace selector for the active organization.
