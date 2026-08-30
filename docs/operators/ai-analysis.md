# AI Analysis — Operator Guide

**AI Analysis** lets you interrogate the discovery candidate bank in plain English: the bank's rows on the left, a chat beside them. Ask *"top 5 by views"* or *"which accounts contribute the most engagement?"* and get an answer you can check against the table without leaving the page.

Find it under **Content → AI Analysis**, next to Single Analyzer and Generator.

---

## The one thing to understand first

**The model never does the arithmetic.**

Every ranking, count and total this tool reports is computed by the system, in code, over the **whole** bank. The AI does exactly two small jobs:

1. it reads your question and picks **which** of a fixed set of queries to run, and with what parameters;
2. it writes two or three sentences describing the rows that query returned.

This is not a stylistic choice. An LLM handed a table of candidates and asked to rank it returns a confident ordering that is frequently wrong, and it fails silently — nothing in the answer tells you it only looked at a fraction of your data. Because the bank grows without limit, a "dump the rows into the prompt" design would degrade into *"the top 5 of an arbitrary subset"* the moment the bank outgrew the context window, and you would never see the moment it happened.

So: the assistant chooses the question, the system answers it. Every answer names the query that ran, how many candidates matched, and cites the candidates it used — with the author handle and a link to the original video — so you can verify it yourself.

---

## What it can answer

| Query | What it does | Example question |
|---|---|---|
| **Top candidates by a metric** | Ranks matching candidates by one stored metric and returns the strongest. | *Top 5 by views* |
| **How many candidates match** | Counts the matching candidates across the whole bank. | *How many TikTok candidates do we have?* |
| **Sum, average, maximum or minimum of a metric** | One aggregate of one metric over the matching candidates. | *What is the average view count?* |
| **Breakdown by network** | Candidate count and metric total per network. | *How is the bank split across networks?* |
| **Breakdown by author** | Groups by author handle and ranks the accounts by metric total. | *Which accounts contribute the most views?* |
| **Most recently discovered candidates** | Lists matching candidates by observation date, newest first. | *What did discovery find most recently?* |

**Metrics**: views, likes, comments, shares, saves, engagement (likes + comments + shares + saves), opportunity score.

**Filters** any query can apply: network (`instagram`, `tiktok`, `youtube`), hashtag, author handle, and a published-date range.

---

## What happens when a question falls outside that list

The tool says so, plainly, and **runs nothing**. You get:

> That question does not map to any analysis this tool can run over the discovery candidate bank. Here is what it can answer: Top candidates by a metric (e.g. "Top 5 by views"); How many candidates match (e.g. "How many TikTok candidates do we have?"); …

followed by the full list of supported queries, right there in the chat.

There is no partial answer and no improvisation. Two things trigger this:

- **The question maps to no supported query** — *"which of these will go viral next week?"*, *"summarise the comments"*. The bank does not hold that, and guessing would be worse than refusing.
- **A parameter is outside the allowed domain** — an unknown network, an unparseable date, an inverted date range. The wording differs slightly ("that question uses a filter this tool cannot apply") but the outcome is identical: nothing executes.

This is the intended behaviour, not a fault. A refusal costs you one cheap AI call and tells you the truth; an improvised answer costs you a bad generation-budget decision.

---

## Scope

This first version reads **only the discovery candidate bank** (`discovered_candidates`) — the candidates niche search and profile mining have banked for your organization.

It does **not** read Single Analyzer analyses or generated content. Questions about those get the "unsupported" reply above.

## When the bank is empty

If your organization has never banked a candidate, the tool says so and points at the route that fills it — **Single Analyzer → By Search** — without spending a single AI call. An empty bank is a state, not an error.

---

## Configuration

### AI provider

AI Analysis reuses the AI route you already configured for **Single Analyzer → Content analysis → Analyze content with AI**. There is nothing extra to set up: if Single Analyzer works, this works.

If no AI route is configured for that step, the chat reports the failure rather than answering.

### Cost caps

Bound in configuration under `Social:DiscoveryBankIntelligence`. The defaults are deliberately small; the tool's value is a verifiable shortlist, not a data dump.

| Setting | Default | What it bounds |
|---|---|---|
| `MaxRowsToModel` | `10` | Hard ceiling on how many computed rows (or breakdown buckets) reach the model, whatever the question asked for. Ask for "top 500" and you get the top 10 **of the full bank**, with the true matched count shown beside it. |
| `DefaultLimit` | `5` | The limit used when the question names no count. |
| `MaxHistoryTurns` | `6` | How many previous chat turns are forwarded to the planning call. |
| `MaxQuestionLength` | `500` | Longest accepted question, in characters. |
| `MaxHistoryEntryLength` | `400` | Longest history entry forwarded. |

Leaving the section out of configuration leaves these defaults in force — the row cap can never be unset.

---

## Reading an answer

Each assistant message shows, in order:

1. **The query that ran** — its name, the metric, every filter applied, and how many candidates matched.
2. **The narration** — two or three sentences over the computed rows.
3. **The citations** — each cited candidate's author handle (linking to the original video), its metric value, network and publication date.

The citation summary reads *"3 of 412 matching candidates cited"* whenever the shown rows are a subset. That sentence is the point: it is what stops a capped shortlist from reading as the whole set.

Dates render in the format you chose under **Settings → Preferences**, like everywhere else in the app.

---

## Verifying a ranking yourself

1. Read the query label on the answer — say *Top candidates by a metric — views · 412 candidates matched*.
2. Open the cited links. Those are the exact candidates the ranking used.
3. Cross-check against the table on the left, which lists the same bank with its metrics.

If the numbers in the citations disagree with the table, the bank changed between the two reads (discovery is append-only and runs in the background) — re-ask the question. They cannot disagree because the model rounded, guessed or hallucinated: it never saw enough of the bank to produce a number of its own.
