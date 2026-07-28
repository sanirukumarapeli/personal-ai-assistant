# Step-by-step roadmap — make the Personal AI Assistant work

Use this document as the **implementation playbook**.  
Work **one step at a time** in this Cursor chat. After each step: mark the checkbox, verify, then move on.

**How to use with the assistant**

> “Do Step 1.1” / “I’m stuck on Step 2.2 — help me create the GitHub PAT” / “Implement Step 3”

---

## Legend

| Tag | Meaning |
|---|---|
| **YOU** | External account / portal config you do (browser) |
| **CODE** | Work we do in this repo |
| **VERIFY** | Proof the step actually works |

---

## Phase 0 — Decisions & accounts (do this first)

### Step 0.1 — Lock product choices

- [ ] **YOU:** Choose primary chat channel  
  - Recommended start: **Telegram** (easy on phone)  
  - Alternative: **Web only** (no bot setup)  
  - Later: both
- [ ] **YOU:** Choose mail/calendar for v1  
  - Recommended start: **chat-only** (LLM works before mail)  
  - Next: **Outlook / Microsoft** *or* **Gmail / Google**
- [ ] **YOU:** Choose default GitHub model id  
  - Recommended: `openai/gpt-4o-mini` (cheap/fast for testing)  
  - Or browse: https://github.com/marketplace/models
- [ ] **CODE:** Update `docs/DECISIONS.md` “Still open” → chosen values

**VERIFY:** `DECISIONS.md` has no critical “Open” items for channel + mail strategy.

---

### Step 0.2 — Install local prerequisites

- [ ] **YOU:** .NET 10 SDK installed (`dotnet --version` → starts with `10.`)
- [ ] **YOU:** Git installed
- [ ] **YOU:** Cursor opened on this folder:  
  `Desktop\personal-ai-assistant`
- [ ] **YOU:** (Optional) Docker — not required for v1
- [ ] **YOU:** Code editor terminal can run `dotnet` commands

**VERIFY:**

```powershell
dotnet --version
git status
```

---

### Step 0.3 — GitHub account + Models access

GitHub Models powers the brain of the assistant.

- [ ] **YOU:** Signed in to GitHub (https://github.com)
- [ ] **YOU:** Open GitHub Models / Marketplace models and confirm you can see models  
  - https://github.com/marketplace/models
- [ ] **YOU:** Create a **fine-grained Personal Access Token**  
  - GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens  
  - Permission needed: **Models: Read** (`models:read`)  
  - Copy the token once (starts like `github_pat_...` or similar)
- [ ] **YOU:** Create local `.env` from `.env.example` (never commit `.env`)

```env
GITHUB_TOKEN=your_pat_here
GITHUB_MODEL_ID=openai/gpt-4o-mini
```

**VERIFY (manual API smoke test later in Step 3):** token exists in `.env`, model id includes `publisher/name`.

**Essential links**

- Models catalog: https://github.com/marketplace/models  
- Inference docs: https://docs.github.com/en/rest/models/inference  
- Endpoint to use in code: `https://models.github.ai/inference`

---

## Phase 1 — Runnable .NET app + GitHub Models chat

Goal: from your PC, send a message and get an LLM reply. No Telegram/mail yet.

### Step 1.1 — Scaffold .NET 10 solution

- [ ] **CODE:** Create solution under `src/`  
  Suggested:
  - `PersonalAi.sln`
  - `src/PersonalAi.Api` — ASP.NET Core Web API (.NET 10)
  - `src/PersonalAi.Core` — prompts, chat service abstractions
  - `tests/PersonalAi.Tests` — empty or one smoke test
- [ ] **CODE:** Solution builds with zero features beyond hello endpoint

**VERIFY:**

```powershell
dotnet build
```

---

### Step 1.2 — Config & secrets loading

- [ ] **CODE:** Load `GITHUB_TOKEN` and `GITHUB_MODEL_ID` from env / user secrets / `.env`
- [ ] **CODE:** Fail fast with a clear error if token missing
- [ ] **YOU:** Ensure `.env` is gitignored (already in `.gitignore`)

**VERIFY:** App starts and logs “GitHub Models configured” (without printing the token).

---

### Step 1.3 — Wire GitHub Models (OpenAI-compatible client)

- [ ] **CODE:** Add OpenAI / `Microsoft.Extensions.AI` client packages
- [ ] **CODE:** Point client to:
  - Endpoint: `https://models.github.ai/inference`
  - API key: `GITHUB_TOKEN`
  - Model: `GITHUB_MODEL_ID` (e.g. `openai/gpt-4o-mini`)
- [ ] **CODE:** Implement `ChatAsync(userMessage)` → assistant text

**VERIFY:** Unit/integration or a tiny console/API call returns a non-empty reply.

---

### Step 1.4 — Local chat API

- [ ] **CODE:** `POST /v1/chat` with body `{ "message": "Hello" }`
- [ ] **CODE:** In-memory session/history (enough for v1)
- [ ] **CODE:** Basic system prompt: “You are my personal assistant…”

**VERIFY:**

```powershell
# example once API is running
curl -X POST http://localhost:5xxx/v1/chat -H "Content-Type: application/json" -d "{\"message\":\"Say hi in one sentence\"}"
```

**Exit criteria for Phase 1:** You can chat with the assistant locally via HTTP. Brain works.

---

## Phase 2 — First real channel (talk from phone/browser)

Pick **one** path.

### Path A — Telegram (recommended)

#### Step 2.A.1 — Create Telegram bot (external)

- [ ] **YOU:** Open Telegram, search `@BotFather`
- [ ] **YOU:** `/newbot` → choose name + username
- [ ] **YOU:** Copy the **bot token**
- [ ] **YOU:** Add to `.env`:

```env
TELEGRAM_BOT_TOKEN=123456:ABC...
```

- [ ] **YOU:** (Optional) `/setcommands` for menu hints later

**VERIFY:** Token looks valid; BotFather shows the bot exists.

#### Step 2.A.2 — Telegram adapter in the app

- [ ] **CODE:** Telegram channel project or hosted service that:
  - receives updates (long polling first — easiest locally)
  - maps Telegram message → `UserTurn`
  - calls chat service
  - replies in the same chat
- [ ] **CODE:** Ignore non-text / handle errors politely

**VERIFY:** Send “hello” to your bot on your phone → get LLM reply.

> **Note:** Long polling works on local PC. Webhooks need a public HTTPS URL (ngrok/cloud) — do webhooks later if needed.

---

### Path B — Web chat

#### Step 2.B.1 — Simple web UI

- [ ] **CODE:** Minimal chat page (static HTML or Blazor/Razor) calling `/v1/chat`
- [ ] **CODE:** Show message history in browser

**VERIFY:** Open `https://localhost:...` and chat successfully.

---

### Step 2.2 — Session identity (personal)

- [ ] **CODE:** Map Telegram user id (or web session cookie) → one personal conversation store
- [ ] **CODE:** Persist history lightly (SQLite file is fine for personal use)

**VERIFY:** Second message remembers the first (“my name is …”).

**Exit criteria for Phase 2:** You can use the assistant from Telegram **or** browser daily.

---

## Phase 3 — Mail & calendar (external apps + tools)

Do this only after Phase 1–2 work. Choose **one** provider first.

### Path M — Microsoft Outlook / personal Microsoft account

#### Step 3.M.1 — Entra app registration (external)

Even for personal Outlook, you need an app registration.

- [ ] **YOU:** Go to https://portal.azure.com → Microsoft Entra ID → App registrations → **New registration**
- [ ] **YOU:** Name: `Personal AI Assistant`
- [ ] **YOU:** Supported account types: personal Microsoft accounts **or** “accounts in any org and personal” (as needed)
- [ ] **YOU:** Add redirect URI for local auth (e.g. `https://localhost:5xxx/signin-oidc` or device-code flow — decide at implement time)
- [ ] **YOU:** Create client secret **or** certificate → store in `.env`
- [ ] **YOU:** API permissions (delegated), start small:
  - `User.Read`
  - `Mail.Read`
  - `Mail.ReadWrite`
  - `Mail.Send` (only when ready to send)
  - `Calendars.Read`
  - `Calendars.ReadWrite`
  - `Tasks.ReadWrite` (if using Microsoft To Do)
- [ ] **YOU:** Grant consent for your account
- [ ] **YOU:** Put into `.env`:

```env
AZURE_TENANT_ID=...
AZURE_CLIENT_ID=...
AZURE_CLIENT_SECRET=...
```

**VERIFY:** App registration shows Client ID; secret saved only in `.env`.

#### Step 3.M.2 — Graph sign-in + token store

- [ ] **CODE:** OAuth login / device code for **your** user
- [ ] **CODE:** Store refresh token encrypted locally
- [ ] **CODE:** Graph client helper

**VERIFY:** API can list your last 5 email subjects.

#### Step 3.M.3 — Mail tools for the assistant

- [ ] **CODE:** Tools: `ListRecentMail`, `SummarizeThread`, `CreateDraft`
- [ ] **CODE:** `SendMail` only after explicit confirm in chat
- [ ] **CODE:** Wire tools into the LLM tool-calling loop

**VERIFY:** In Telegram/Web: “Summarize my unread mail” works.

#### Step 3.M.4 — Calendar + reminders

- [ ] **CODE:** Tools: `GetAgenda`, `CreateEvent`, `CreateReminder`
- [ ] **VERIFY:** “What’s on my calendar tomorrow?” and “Remind me Friday to call Mom”

---

### Path G — Gmail / Google Calendar

#### Step 3.G.1 — Google Cloud OAuth (external)

- [ ] **YOU:** https://console.cloud.google.com → create/select project
- [ ] **YOU:** Enable **Gmail API** + **Google Calendar API**
- [ ] **YOU:** Configure OAuth consent screen (External / testing, add your Gmail as test user)
- [ ] **YOU:** Create OAuth client ID (Desktop or Web)
- [ ] **YOU:** Download client secrets → map into `.env` (`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`)

**VERIFY:** OAuth client exists; APIs enabled.

#### Step 3.G.2–3.G.4

Same as Microsoft path but with Gmail/Calendar APIs: sign-in → list mail → tools → calendar/reminders.

---

**Exit criteria for Phase 3:** Assistant can read mail/calendar and draft/send with confirmation.

---

## Phase 4 — Documents (PDF / Word / Excel)

### Step 4.1 — Markdown / simple file export

- [ ] **CODE:** “Save this as a note” → `.md` file in a local `data/notes` folder
- [ ] **VERIFY:** File appears and content is correct

### Step 4.2 — PDF generation (usable quality)

- [ ] **CODE:** HTML template → PDF (Playwright or similar) **or** simple PDF library for v1
- [ ] **CODE:** Tool: `CreatePdf(title, sections)`
- [ ] **CODE:** Return file path or Telegram document upload

**VERIFY:** “Make a one-page PDF checklist for my trip” → you receive a file.

### Step 4.3 — Word / Excel (optional)

- [ ] **CODE:** Open XML / ClosedXML generators
- [ ] **VERIFY:** Create `.docx` / `.xlsx` on request

**Exit criteria for Phase 4:** Assistant can produce at least PDF (or md) you can download/share.

---

## Phase 5 — Make it reliable for daily use

### Step 5.1 — Persistence & backup

- [ ] **CODE:** SQLite (or similar) for sessions, tasks, token store
- [ ] **YOU:** Decide backup location for `data/` folder

### Step 5.2 — Safety defaults

- [ ] **CODE:** Never send email / create external invite without “yes”
- [ ] **CODE:** Redact tokens from logs
- [ ] **CODE:** Rate-limit Telegram replies on errors

### Step 5.3 — Run on startup (optional)

- [ ] **YOU:** Windows Task Scheduler / service to start API + Telegram poller at login
- [ ] **VERIFY:** Reboot → bot still answers

### Step 5.4 — Eval smoke checklist

Keep a short personal test list:

1. Chat hello  
2. Remember prior message  
3. Summarize mail (if enabled)  
4. Create reminder  
5. Create PDF  

---

## Essential external checklist (all portals)

Print this mentally — without these, the system cannot fully work:

| # | External thing | Needed for | Status |
|---|---|---|---|
| 1 | GitHub account | LLM | required |
| 2 | GitHub PAT (`models:read`) | GitHub Models API | required |
| 3 | Chosen model id (`publisher/model`) | Inference | required |
| 4 | Telegram BotFather token | Phone chat | required if Telegram path |
| 5 | Public HTTPS URL / ngrok | Telegram **webhooks** only | optional if using long polling |
| 6 | Entra app + secret + Graph permissions | Outlook mail/calendar | required if Microsoft path |
| 7 | Google Cloud OAuth + Gmail/Calendar APIs | Gmail path | required if Google path |
| 8 | (Later) Hosting VM / always-on PC | 24/7 bot | optional for v1 |

---

## Recommended build order (default path)

If you want the fastest working assistant:

1. **Phase 0** — decisions + GitHub PAT  
2. **Phase 1** — .NET 10 + GitHub Models local chat  
3. **Phase 2 Path A** — Telegram long polling  
4. **Phase 3** — pick Outlook **or** Gmail (one only)  
5. **Phase 4** — PDF  
6. **Phase 5** — harden for daily use  

Do **not** start mail/PDF before local chat works.

---

## Current position

| Phase | Status |
|---|---|
| 0 — Foundation folder/docs | **Done** (repo exists) |
| 0.1 — Lock channel + mail choices | **Not done** |
| 0.3 — GitHub PAT in `.env` | **Not done** (you) |
| 1+ — Code | **Not started** |

---

## Next action right now

1. **You:** Complete Step 0.1 (channel + mail + model) and Step 0.3 (GitHub PAT → `.env`)  
2. **Then in this chat:** say **“Implement Step 1.1”** to scaffold the .NET 10 solution  

When a step is finished, check its box in this file so progress stays visible.
