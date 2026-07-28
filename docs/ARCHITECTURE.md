# Architecture — Personal AI Assistant

## Goal

A **personal** AI assistant that reduces everyday busywork for one user:

- Chat (Telegram / Web)
- Mail triage, drafts, reminders (later)
- Calendar awareness and meeting helpers (later)
- Simple document / PDF generation (later)

Not a company multi-agent platform.

## High-level diagram

```text
You (Telegram / Web)
        │
        ▼
Personal Assistant API (.NET 10)
  - chat session
  - planner / tool calling
        │
   ┌────┼────┬──────────┐
   ▼    ▼    ▼          ▼
 Mail  Cal  Tasks     Docs/PDF   (tools — add gradually)
        │
        ▼
 GitHub Models (LLM)
 Endpoint: https://models.github.ai/inference
 Model id: publisher/model  (e.g. openai/gpt-4o-mini)
```

## Design principles

1. **One assistant + tools** first — not a fleet of domain agents.
2. **LLM is pluggable** — GitHub Models via OpenAI-compatible client.
3. **Channel adapters** only translate messages; business logic stays in the API.
4. **Confirm before send** for mail / external actions (even for personal use).
5. **Secrets in env / local secrets**, never committed.

## Components (current)

| Component | Responsibility |
|---|---|
| `PersonalAi.Api` | ASP.NET Core host, `/health`, `/v1/chat`, Google auth, Telegram poller |
| `PersonalAi.Core` | Chat + tool calling, GitHub Models, Gmail services, SQLite sessions/tokens |
| `apps/web` | Next.js chat UI + Connect Gmail |
| Calendar / docs | Deferred — see `DEFERRED.md` |

## GitHub Models integration (planned)

- Base URL: `https://models.github.ai/inference`
- Auth header: Bearer `GITHUB_TOKEN`
- Model: `GITHUB_MODEL_ID` (must include publisher prefix)
- Prefer `Microsoft.Extensions.AI` + OpenAI client so providers can change later

## What we explicitly dropped from the enterprise design

- S&OP / Finance / HR agents
- Copilot Studio Direct Line bridges
- Entra company admin consent / Teams org publish (unless you choose Teams later)
- Heavy audit / Purview / RBAC
- AWS Bedrock
