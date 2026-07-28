# Decisions

Record of choices so future implementation stays consistent.

| Date | Decision | Choice | Notes |
|---|---|---|---|
| 2026-07-28 | Product focus | Personal use (not C-suite / company) | No S&OP, Finance, Copilot Studio |
| 2026-07-28 | Runtime | .NET 10 | SDK `10.0.300` |
| 2026-07-28 | LLM provider | GitHub Models | OpenAI-compatible API |
| 2026-07-28 | LLM endpoint | `https://models.github.ai/inference` | Not the old Azure endpoint |
| 2026-07-28 | Model id | `openai/gpt-5` | GitHub Models catalog id |
| 2026-07-28 | Agent style | Single assistant + tools | Multi-agent only if needed later |
| 2026-07-28 | Project location | `Desktop/personal-ai-assistant` | Cursor workspace root |
| 2026-07-28 | Frontend | Next.js App Router + TypeScript | `apps/web` |
| 2026-07-28 | First channel | Web (Next.js) | Then Telegram |
| 2026-07-28 | Second channel | Telegram long polling | After Web works |
| 2026-07-28 | Mail & calendar | Chat-only first | Outlook/Gmail later |
| 2026-07-28 | Hosting v1 | Local PC | API + `next dev` |
| 2026-07-28 | Build style | One step at a time | Pause for verify between steps |

## Still open (later phases only)

| Topic | Status |
|---|---|
| Mail provider when added | Outlook vs Gmail — decide at mail phase |
| Always-on hosting | Optional later |
