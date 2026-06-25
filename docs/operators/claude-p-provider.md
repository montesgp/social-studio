# Claude -p Provider Setup

`claude-p` is a backend-only integration provider for Claude Code CLI (`claude -p`). It can be enabled for `AiModel` and `MediaGeneration` workflow routes without exposing shell execution to the browser.

Safe connector fields:
- `executable_path`: executable name or absolute path, default `claude`; shell fragments are rejected.
- `working_directory`: optional existing backend directory.
- `allowed_output_directory`: required when image output returns `{ "mime": "...", "filePath": "..." }`; files must resolve inside this directory.
- `default_model`, `timeout_ms`, `output_format`, `permission_mode`, `allowed_tools`, `json_schema`, `settings_json`.

Image generation stdout must be JSON in one of these forms:
- `{ "mime": "image/png", "base64": "..." }`
- `{ "mime": "image/png", "filePath": "..." }`

The adapter uses `ProcessStartInfo.ArgumentList`, disables shell execution, captures stderr, and enforces timeout/cancellation in Infrastructure only.
