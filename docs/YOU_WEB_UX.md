# Web UX — sessions, stop, voice, search

## Stop a reply

While the assistant is thinking, the Send button becomes **Stop**. Click it to cancel the request. You should see “Stopped.” in the chat.

## Chat sessions

- Left **Chats** rail lists past sessions from SQLite.
- **New** starts a blank chat.
- Click a session to reload its history.
- Trash icon deletes a session.
- Active session id is stored in `localStorage`.

Toggle the rail with the panel button next to the title.

## Files panel

**Files** in the header opens recent notes/PDFs/Office/uploads from `GET /v1/documents`, with download links.

## Voice mode (Chrome / Edge recommended)

- Background listening starts after the first click on the page (browser mic permission).
- Say **Hello** or **Hi**, or tap the **mic**, to enter voice mode.
- Composer becomes a Claude-style **Listening…** bar with a center waveform (dots when quiet, bars when speaking), plus **X** (cancel) and blue **✓** (send now).
- Speak your question; after ~**2 seconds** of silence it auto-sends (or tap ✓ sooner).
- Replies are **spoken only in voice mode**. Typed chat stays silent.
- Per-message **speaker** under assistant replies speaks that message anytime.

Safari/Firefox may have limited or no support.

## Message actions

- **User** (hover): copy, edit (truncate history + resend), send time.
- **Assistant**: copy, speak.

## Confirm / Cancel

When the assistant stages something that needs approval (calendar create/update/cancel, send a mail draft, overwrite a document, delete a note), **Confirm** and **Cancel** buttons appear above the composer (including in voice mode).

- Click **Confirm** / **Cancel** — applied immediately (no need to type).
- Or type / say natural phrases (yes, yep, go ahead, cancel, never mind, …) — the agent should confirm or discard the pending action.

## Web search / URL fetch

The model can call:

- `web_search` — DuckDuckGo HTML results (no API key)
- `fetch_url` — read an `http`/`https` page (localhost/private IPs blocked)

Examples: “Search for Gemini API rate limits”, “Summarize https://example.com/article”.

## Related

- Documents: [YOU_DOCUMENTS.md](./YOU_DOCUMENTS.md)
- Gemini setup: [YOU_STEP_0.md](./YOU_STEP_0.md)
