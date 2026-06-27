# Claude -p Provider Setup

`claude-p` is a backend-only integration provider for Claude Code CLI (`claude -p`). It can be enabled for `AiModel` and `MediaGeneration` workflow routes without exposing shell execution to the browser. It runs on the **application host** (the same machine where Incoders Hive runs), so it needs **no per-organization credentials**.

## Auto-provisioning (zero-config)

On startup, every **active** organization gets a `claude-p` integration seeded automatically in
**Managed by Incoders** mode and **Connected** status — so it is immediately selectable in the AI-model
stages of the content-analysis workflow without any manual setup. The seed is idempotent and only touches
`claude-p`: organizations that already configured it (or any other provider) are never overwritten.
(Seeder: `ClaudePromptCliOrganizationSeeder`, wired in `Program.cs`.)

## Test connection (real probe)

"Test connection" runs a **real** host probe for `claude-p`: it executes `claude --version` via the
backend `IProcessRunner` and only reports success when the CLI launches and exits 0. A missing executable,
a non-zero exit, or a timeout reports failure. (Health check: `ClaudePromptCliIntegrationHealthCheck`,
ProviderKey `claude-p`.) Note: the probe runs for **Managed by Incoders** and **Bring your own key** modes;
**System default** is treated as trusted by the connection tester and skips the live probe.

In `/settings/integrations`, the **Claude -p** card supports three connection modes:

- **Managed by Incoders** — default for the auto-seeded integration; runs the live CLI probe.
- **Bring your own key** — also runs the live CLI probe.
- **System default (host)** — trusts the host `claude` without a live probe.

No credential fields are required in any mode.

## Model sync

The Claude Code CLI has **no model-enumeration command**, so "Sync models" for `claude-p` resolves the list
in two tiers (`ClaudePromptCliModelCatalogClient`):

1. **Live** — if an Anthropic API key is available (the `api_key` connector credential, or the host
   `ANTHROPIC_API_KEY` environment variable), it queries the live Anthropic `/v1/models` catalog, so new
   models (e.g. Opus 4.8) appear automatically — the same source as the API-keyed Claude connector.
2. **Curated fallback** — otherwise (or if the live call fails), it returns the curated current set in
   `ClaudePromptCliSettings.DefaultTextModels`. Update that list when a new model ships.

This also seeds the initial model menu, so the latest models are selectable even before the first sync.

## Host configuration (operator-level, not per-org)

Host tuning is NOT exposed as organization credentials. It falls back to the safe defaults in `ClaudePromptCliSettings` on the host:

- `executable_path`: defaults to `claude` (resolved from PATH); shell fragments are rejected.
- `working_directory`: optional existing backend directory.
- `allowed_output_directory`: used for `filePath` image output; generated files must resolve inside this directory.
- `default_model`, `timeout_ms` (default 120000), `output_format` (`text`/`json`), `permission_mode`, `allowed_tools`, `json_schema`, `settings_json`.

Image generation stdout must be JSON in one of these forms:
- `{ "mime": "image/png", "base64": "..." }`
- `{ "mime": "image/png", "filePath": "..." }`

The adapter uses `ProcessStartInfo.ArgumentList`, disables shell execution, captures stderr, and enforces timeout/cancellation in Infrastructure only.
