# Roadmap (summary)

## Status

- [x] Chat + GitHub Models (`openai/gpt-5`)
- [x] Next.js web UI
- [x] SQLite sessions
- [x] Telegram long-polling (optional token)
- [x] **Gmail OAuth + mail tools** (unread, search, drafts, draft+confirm-send)
- [ ] Calendar (later)
- [ ] Documents/PDF (later)

## Run

```powershell
# Terminal 1
dotnet run --project src/PersonalAi.Api

# Terminal 2
cd apps/web
npm run dev
```

## Gmail

1. Put `GOOGLE_CLIENT_ID` + `GOOGLE_CLIENT_SECRET` in `.env` — see [YOU_GMAIL_SETUP.md](./YOU_GMAIL_SETUP.md)
2. Open http://localhost:3000 → **Connect Gmail**
3. Chat examples:
   - What are my pending emails?
   - Find emails about invoices
   - What drafts do I have?
   - Draft an email to … / Yes, send it

## Docs

- [YOU_GMAIL_SETUP.md](./YOU_GMAIL_SETUP.md)
- [ARCHITECTURE.md](./ARCHITECTURE.md)
- [DECISIONS.md](./DECISIONS.md)
- [INDEX.md](./INDEX.md)
