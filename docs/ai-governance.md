# AI Governance

## Purpose
Defines how AI models, prompts, agents, evaluations, and automated decisions are governed.

## Rules
- Every AI-generated business artifact must be traceable to input data, prompt version, model, and evaluation result.
- High-impact user-facing publication decisions should support human review before external publishing.
- Model selection must be explicit and configurable.
- Keep provider-specific concerns behind abstractions.
- Store prompt versions and evaluation criteria when outputs affect client deliverables.

## Quality Gates
- Prompt has clear purpose and expected output schema.
- Evaluation criteria exists.
- Fallback model/provider strategy is documented.
- Cost and latency assumptions are visible.
