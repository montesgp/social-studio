# User Preferences Spec

## Purpose
Define per-user UI preferences that persist across sessions/devices without introducing tenant-global theme settings.

## Capabilities
- Read the current authenticated user preference profile.
- Update the current authenticated user preference profile.
- Support `theme_key` values:
  - `hive-core`
  - `hive-social`
  - `system`
- Persist preference records per user in `platform.user_preferences`.

## Constraints
- Preferences are user-scoped, not tenant/global.
- Theme values must be validated against the supported set.
- Domain/Application remain provider-agnostic.

## Acceptance Criteria
- An authenticated user can choose light/dark theme from a minimal selector in the dashboard user section.
- Theme choice persists across browser sessions and devices for the same user.
- The dashboard surfaces theme update/sync failures and successful updates through in-app notifications.
- Existing UI remains functional under both Hive Core and Hive Social visual tokens.
