# ADR 0006 - Content Studio Editability Derives From the Domain Transition Table

## Status
Accepted

## Context

Content Studio disabled every editing control for items in `Discarded`, `ReadyForPublication` and
`HandedOff`, via a hardcoded `TERMINAL` list in the frontend panel.

That list contradicted the domain. `ReviewStateTransitions.IsTerminal` treats only `HandedOff` as
terminal, and the transition table explicitly permits `Discarded → InReview` ("recover") and
`ReadyForPublication → Approved` (un-stage). Nothing in the product surfaced either transition, so a
piece that the domain considered recoverable was, in practice, permanently frozen.

The failure mode was worse than the restriction itself. Caption, post copy and every slide textbox
derive from a single `canEdit` flag, so all of them went dead at once with no explanation anywhere
in the UI — indistinguishable from a studio that had failed to initialize. The behaviour was
reported as a bug ("the piece opens but nothing is editable"); investigation against the database
showed the piece was simply `Discarded`.

## Decision

1. **Editing stays blocked in those states.** This is not a UI restriction to be relaxed: `Edited` is
   unreachable from `Discarded`, `ReadyForPublication` and `HandedOff` in the transition table, so the
   server would reject the write. The UI must not offer an edit the domain will refuse.

2. **The block must state its reason and offer the way out.** A locked piece renders an explicit
   notice naming its state, plus the transition the domain already allows:
   - `Discarded` → **Recover** (`Discarded → InReview`), exposed as `ReopenItemCommand` and
     `POST /api/social/content-studio/items/{id}/reopen`.
   - `ReadyForPublication` → **Back to approved** (`ReadyForPublication → Approved`), which the
     existing approve command already performs.
   - `HandedOff` → no action. It is the only genuinely terminal state.

3. **The transition table is the single authority.** Any future surface that decides whether an
   operation is offerable derives it from `ReviewStateTransitions`, never from a duplicated list of
   state names. A frontend constant that enumerates states is a copy that will drift, and this one
   did.

## Consequences

- A discarded piece is recoverable through the product, matching what the domain always allowed.
- The frontend `TERMINAL` constant remains, but strictly as "states from which `Edited` is
  unreachable" — documented as such, and paired with the lock reasons that explain each one. It is no
  longer a claim about terminality.
- One new application command and endpoint; no domain change was required, because the transition
  already existed.
- The general lesson is recorded here rather than in a commit message: when a UI gate and a domain
  rule disagree, the domain wins, and the gap is usually a missing affordance rather than a missing
  rule.

## Related
- Builds on: ADR 0004 (Content Studio review model), which defined the guarded review lifecycle
- Domain: `SocialStudio.Domain/Social/ContentStudio/ReviewStateTransitions.cs`
- Application: `Features/Social/ContentStudio/Operations/ReopenItemCommand.cs`
