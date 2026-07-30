# Roadmap (summary)

## Status

- [x] Chat + Google Gemini (`gemini-3.5-flash-lite`)
- [x] Next.js web UI
- [x] SQLite sessions
- [x] Discord bot Gateway (optional token; preferred phone channel)
- [x] Telegram long-polling (optional token)
- [x] **Gmail OAuth + mail tools** (unread, search, drafts, draft+confirm-send)
- [x] **Google Calendar tools** (list/search/get, availability, create/update/cancel with confirm)
- [x] **Documents** (notes, PDF/Word/Excel/PowerPoint create, upload/extract/summarize/Q&A, rewrite with confirm)
- [x] **Images** (upload + analyze via chat model; generate via `GEMINI_IMAGE_MODEL_ID`, no confirm)
- [x] Web UX: session list, stop reply, files panel, mic/TTS, web_search + fetch_url
- [ ] OCR / Drive sync (later)

## Run

```powershell
# Terminal 1
dotnet run --project src/PersonalAi.Api

# Terminal 2
cd apps/web
npm run dev
```

## Gmail + Calendar

1. Put `GOOGLE_CLIENT_ID` + `GOOGLE_CLIENT_SECRET` in `.env` — see [YOU_GMAIL_SETUP.md](./YOU_GMAIL_SETUP.md)
2. Enable Calendar API + scopes — see [YOU_CALENDAR_SETUP.md](./YOU_CALENDAR_SETUP.md)
3. Open http://localhost:3000 → **Connect Gmail** (grants mail + calendar; reconnect after adding calendar scopes)
4. Chat examples:
   - What are my pending emails?
   - Find emails about invoices
   - What drafts do I have?
   - Draft an email to … / Yes, send it
   - What’s on my calendar tomorrow?
   - Am I free Friday 2–3pm?
   - Create a 30-minute meeting tomorrow at 10am titled Team sync / Yes
   - Move Team sync to 11am / Confirm
   - Cancel Team sync / Yes

## Docs

- [YOU_GMAIL_SETUP.md](./YOU_GMAIL_SETUP.md)
- [YOU_CALENDAR_SETUP.md](./YOU_CALENDAR_SETUP.md)
- [ARCHITECTURE.md](./ARCHITECTURE.md)
- [DECISIONS.md](./DECISIONS.md)
- [INDEX.md](./INDEX.md)
