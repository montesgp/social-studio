# Content Research Spec

## Purpose
Content research retrieves candidate high-performing content by client, niche, reference accounts or own account sources.

## Requirements
- Hive Social clients MUST be scoped to an organization with `organization_id`.
- A user MUST select or resolve an organization before creating/listing social clients.
- The system MUST persist clients in the `social` module schema.
- Client lookup/list operations MUST NOT return clients from another organization.
- Single-content analysis workflows MAY run without a selected client; in that case generated mutations MUST preserve the source video's original positioning without client-specific orientation.
- When a single-content analysis workflow offers client selection, selectable clients MUST be active and scoped to the resolved organization.

## Events
- TBD

## Acceptance Criteria
- A CM can create a client for the resolved organization.
- A CM can list clients for the resolved organization.
- A client id from another organization returns not found.
- A CM can run single-content analysis with no client selected and receive neutral mutations based on the original video.
- A CM can choose an active client in the resolved organization before generating single-content mutations.
