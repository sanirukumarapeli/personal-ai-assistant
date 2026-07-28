# Personal AI Assistant

Personal assistant for everyday use — **.NET 10** API, **Next.js** web UI, **Google Gemini** (`gemini-3.5-flash-lite`).

## Stack (locked)

| Layer | Choice |
|---|---|
| API | .NET 10 (`PersonalAi.Api` + `PersonalAi.Core`) |
| Frontend | Next.js App Router + TypeScript (`apps/web`) |
| LLM | Google Gemini (AI Studio) via OpenAI-compatible API |
| Model | `gemini-3.5-flash-lite` (free-tier ~15 RPM / 500 RPD) |
| Channels | Web first, then Discord (Telegram optional) |
| Mail / Calendar | Gmail + Google Calendar tools |

## How we build

One step at a time — see [docs/STEP_BY_STEP.md](docs/STEP_BY_STEP.md) and the guided plan.

1. You configure externals (Gemini key, Google OAuth, bot token, …)
2. We implement that step’s code
3. You verify
4. Continue only when ready

## Quick start

1. Put your Gemini API key in `.env` (`GEMINI_API_KEY=...`) — [AI Studio](https://aistudio.google.com/apikey)
2. Run API: `dotnet run --project src/PersonalAi.Api`
3. Run Web: `cd apps/web && npm run dev`
4. Open http://localhost:3000

## Secrets

| Variable | Purpose |
|---|---|
| `GEMINI_API_KEY` | Google AI Studio API key |
| `GEMINI_MODEL_ID` | `gemini-3.5-flash-lite` (fallback `gemini-3.1-flash-lite`) |
| `GEMINI_ENDPOINT` | Optional; default OpenAI-compat URL |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | Gmail + Calendar OAuth |
| `DISCORD_BOT_TOKEN` | Discord bot token ([YOU_DISCORD_SETUP.md](docs/YOU_DISCORD_SETUP.md)) |
| `DISCORD_ALLOWED_USER_IDS` | Your Discord user id(s), comma-separated |
| `TELEGRAM_BOT_TOKEN` | Optional; leave empty if using Discord |

Create Gemini key: https://aistudio.google.com/apikey

## Docs

- [Architecture](docs/ARCHITECTURE.md)
- [Decisions](docs/DECISIONS.md)
- [Roadmap](docs/ROADMAP.md)
- [Step-by-step](docs/STEP_BY_STEP.md)
- [YOU_STEP_0 — Gemini key](docs/YOU_STEP_0.md)
