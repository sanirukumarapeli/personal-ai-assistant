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
 Google Gemini (LLM — AI Studio)
 Endpoint: https://generativelanguage.googleapis.com/v1beta/openai/
 Model id: gemini-2.5-flash
```

## Design principles

1. **One assistant + tools** first — not a fleet of domain agents.
2. **LLM is pluggable** — Gemini via OpenAI-compatible client (same pattern for other providers later).
3. **Channel adapters** only translate messages; business logic stays in the API.
4. **Confirm before send** for mail / external actions (even for personal use).
5. **Secrets in env / local secrets**, never committed.

## Components (current)

| Component | Responsibility |
|---|---|
| `PersonalAi.Api` | ASP.NET Core host, `/health`, `/v1/chat`, Google auth, Telegram poller |
| `PersonalAi.Core` | Chat + tool calling, Gemini, Gmail/Calendar services, SQLite sessions/tokens |
| `apps/web` | Next.js chat UI + Connect Google |

## Gemini integration

- Base URL: `https://generativelanguage.googleapis.com/v1beta/openai/`
- Auth header: Bearer `GEMINI_API_KEY`
- Model: `GEMINI_MODEL_ID` (default `gemini-2.5-flash`)
- Key from [Google AI Studio](https://aistudio.google.com/apikey) — separate from Gmail OAuth
- Prefer `Microsoft.Extensions.AI` later so providers can change easily

## What we explicitly dropped from the enterprise design

- S&OP / Finance / HR agents
- Copilot Studio Direct Line bridges
- Entra company admin consent / Teams org publish (unless you choose Teams later)
- Heavy audit / Purview / RBAC
- AWS Bedrock
- GitHub Models (replaced by Gemini for free-tier headroom)
