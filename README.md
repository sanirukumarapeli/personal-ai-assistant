# Personal AI Assistant

Personal assistant for everyday use — **.NET 10** API, **Next.js** web UI, **GitHub Models** (`openai/gpt-5`).

## Stack (locked)

| Layer | Choice |
|---|---|
| API | .NET 10 (`PersonalAi.Api` + `PersonalAi.Core`) |
| Frontend | Next.js App Router + TypeScript (`apps/web`) |
| LLM | GitHub Models → `https://models.github.ai/inference` |
| Model | `openai/gpt-5` |
| Channels | Web first, then Telegram |
| Mail | Chat-only until a later phase |

## How we build

One step at a time — see [docs/STEP_BY_STEP.md](docs/STEP_BY_STEP.md) and the guided plan.

1. You configure externals (PAT, bot token, …)
2. We implement that step’s code
3. You verify
4. Continue only when ready

## Quick start (after Steps 0–3)

1. Put your GitHub PAT in `.env` (`GITHUB_TOKEN=...`)
2. Run API: `dotnet run --project src/PersonalAi.Api`
3. Run Web: `cd apps/web && npm run dev`
4. Open http://localhost:3000

## Secrets

| Variable | Purpose |
|---|---|
| `GITHUB_TOKEN` | Fine-grained PAT with **models:read** |
| `GITHUB_MODEL_ID` | `openai/gpt-5` |
| `TELEGRAM_BOT_TOKEN` | From `@BotFather` (Step 4) |

Create PAT: https://github.com/settings/tokens?type=beta

## Docs

- [Architecture](docs/ARCHITECTURE.md)
- [Decisions](docs/DECISIONS.md)
- [Roadmap](docs/ROADMAP.md)
- [Step-by-step](docs/STEP_BY_STEP.md)
