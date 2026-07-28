# Decisions

Record of choices so future implementation stays consistent.

| Date | Decision | Choice | Notes |
|---|---|---|---|
| 2026-07-28 | Product focus | Personal use (not C-suite / company) | No S&OP, Finance, Copilot Studio |
| 2026-07-28 | Runtime | .NET 10 | SDK `10.0.300` |
| 2026-07-28 | LLM provider | Google Gemini (AI Studio) | OpenAI-compatible API |
| 2026-07-28 | LLM endpoint | `https://generativelanguage.googleapis.com/v1beta/openai/` | AI Studio OpenAI-compatible |
| 2026-07-28 | Model id | `gemini-3.5-flash-lite` | Free-tier Rate limits: ~15 RPM / 500 RPD (same class as 3.1 Lite). Full Flash often ~5 RPM / 20 RPD — too tight for tool loops. Avoid `gemini-2.5-flash` for new keys (404) |
| 2026-07-28 | Agent style | Single assistant + tools | Multi-agent only if needed later |
| 2026-07-28 | Project location | `Desktop/personal-ai-assistant` | Cursor workspace root |
| 2026-07-28 | Frontend | Next.js App Router + TypeScript | `apps/web` |
| 2026-07-28 | First channel | Web (Next.js) | Then Discord |
| 2026-07-28 | Second channel | Discord Gateway bot | Preferred over Telegram for this project; Telegram still optional |
| 2026-07-28 | Mail & calendar | Gmail + Google Calendar | OAuth tools in chat |
| 2026-07-28 | Hosting v1 | Local PC | API + `next dev` |
| 2026-07-28 | Build style | One step at a time | Pause for verify between steps |

## Still open (later phases only)

| Topic | Status |
|---|---|
| Mail provider when added | Outlook vs Gmail — decide at mail phase |
| Always-on hosting | Optional later |
