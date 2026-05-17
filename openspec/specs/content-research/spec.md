# Content Research Spec

## Purpose
Content research retrieves candidate high-performing content by client, niche, reference accounts or own account sources.

## Requirements
- Hive Social clients MUST be scoped to an organization with `organization_id`.
- A user MUST select or resolve an organization before creating/listing social clients.
- The system MUST persist clients in the `social` module schema.
- Client lookup/list operations MUST NOT return clients from another organization.

## Events
- TBD

## Acceptance Criteria
- A CM can create a client for the resolved organization.
- A CM can list clients for the resolved organization.
- A client id from another organization returns not found.
