# YOU — Discord bot setup (step by step)

Use Discord as your phone channel instead of Telegram. The API code is ready; you only create a Discord app, enable intents, invite the bot, and put the token in `.env`.

**Time:** about 10–20 minutes.  
**Cost:** free for personal use.  
**Requirement:** Discord account + Discord desktop or mobile app.

---

## Checklist (print / tick as you go)

- [ ] 1. Create Discord application
- [ ] 2. Add a Bot user + copy token
- [ ] 3. Enable **Message Content Intent** (required — without this the bot cannot read your messages)
- [ ] 4. Create a private server (recommended)
- [ ] 5. Generate invite URL with correct permissions and invite the bot
- [ ] 6. Find your Discord **User ID** (Developer Mode)
- [ ] 7. Put `DISCORD_BOT_TOKEN` + `DISCORD_ALLOWED_USER_IDS` in `.env`
- [ ] 8. Leave `TELEGRAM_BOT_TOKEN` empty (unless you still want Telegram too)
- [ ] 9. Restart API and confirm logs + `/health`
- [ ] 10. Test: DM the bot **or** `@mention` it in your private server

---

## 1. Create a Discord application

1. Open a browser and go to:  
   **https://discord.com/developers/applications**
2. Sign in with the Discord account you use on your phone.
3. Click **New Application** (top right).
4. Name it something clear, e.g. `Personal AI Assistant`.
5. Agree to the Developer Terms of Service / Policy if prompted.
6. Click **Create**.
7. You land on the app’s **General Information** page.  
   You do **not** need the Application ID for basic chat — keep the page open.

---

## 2. Create the Bot and copy the token

1. In the left sidebar, click **Bot**.
2. Click **Add Bot** → confirm **Yes, do it!** (if Discord asks).
3. Under **Token**, click **Reset Token** (or **View Token** / **Copy** — Discord only shows a full token after reset for new bots).
4. Confirm with 2FA / email if Discord asks.
5. Click **Copy** and paste the token somewhere temporary (Notepad).  
   **Treat it like a password.** Never commit it to GitHub. Never paste it in a public chat.

Optional but recommended on this page:

- **Public Bot**: leave **OFF** (unchecked) so random people cannot invite it easily.
- **Requires OAuth2 Code Grant**: leave **OFF**.

---

## 3. Enable Privileged Gateway Intent (CRITICAL)

Still on the **Bot** page, scroll to **Privileged Gateway Intents**.

Turn **ON** (toggle to blue/green):

| Intent | Required? | Why |
|---|---|---|
| **Presence Intent** | No | Not needed for this app |
| **Server Members Intent** | No | Not needed for this app |
| **Message Content Intent** | **YES** | Without this, Discord strips message text and the bot will ignore you |

1. Toggle **Message Content Intent** → **On**.
2. Click **Save Changes** at the bottom of the page (do not skip this).

If you forget this step, the API may start, but the bot will never “see” your chat text.

---

## 4. Create a private Discord server (recommended)

Bots usually need a shared server before you can DM them reliably.

1. Open the **Discord app** (desktop or phone).
2. In the left server list, click the **+** (Add a Server).
3. Choose **Create My Own** → **For me and my friends** (or equivalent).
4. Name it e.g. `Personal Assistant`.
5. Create it. Keep it **private** (do not invite strangers).
6. Optional: create a text channel named `#assistant` for chatting with the bot via @mention.

---

## 5. Invite the bot to your server

### 5a. Build the invite URL in the Developer Portal

1. Back in the browser: **https://discord.com/developers/applications**
2. Open your application → left sidebar **OAuth2** → **URL Generator**.
3. Under **Scopes**, check **only**:
   - [x] **bot**
4. Under **Bot Permissions**, check:

   **Text Permissions**
   - [x] **View Channels**
   - [x] **Send Messages**
   - [x] **Read Message History**
   - [x] **Attach Files** (optional, useful later)
   - [x] **Embed Links** (optional)

   Do **not** grant Administrator unless you understand the risk.

5. At the bottom, copy the **Generated URL**.

### 5b. Open the URL and add the bot

1. Paste the Generated URL into the same browser (while logged into Discord).
2. Under **Add to Server**, pick your private server (`Personal Assistant`).
3. Confirm the permissions → **Authorize**.
4. Complete the CAPTCHA if shown.
5. In Discord, you should see a system message that the bot joined the server.
6. Confirm the bot appears in the member list (it may show as offline until your API is running).

---

## 6. Get your Discord User ID (for the allowlist)

This locks the bot so **only you** get replies.

1. In Discord: **User Settings** (gear) → **App Settings** → **Advanced**.
2. Turn **Developer Mode** **ON**.
3. Close settings.
4. Right-click **your own avatar / username** (in a server member list, or your profile).
5. Click **Copy User ID**.
6. Paste it into Notepad. It looks like a long number, e.g. `123456789012345678`.

Phone: long-press your name → **Copy User ID** (Developer Mode must be on).

---

## 7. Put values in `.env`

Open the project root file:

`c:\Users\saniru.dewmina\Desktop\personal-ai-assistant\.env`

Add or update (use **your** token and user id):

```env
# Discord (preferred phone channel)
DISCORD_BOT_TOKEN=paste_the_bot_token_here
DISCORD_ALLOWED_USER_IDS=paste_your_user_id_here

# Leave empty unless you also want Telegram
TELEGRAM_BOT_TOKEN=
```

Notes:

- No quotes around the values.
- No spaces around `=`.
- `DISCORD_ALLOWED_USER_IDS` can list several ids separated by commas if needed: `id1,id2`.
- If you leave `DISCORD_ALLOWED_USER_IDS` empty, **anyone** who can message the bot may get replies (API will log a warning).

Save the file.

---

## 8. Restart the API

Stop any running `PersonalAi.Api`, then:

```powershell
cd c:\Users\saniru.dewmina\Desktop\personal-ai-assistant
dotnet run --project src/PersonalAi.Api
```

### Success logs (what you want to see)

Somewhere in the console:

- `Loaded environment file: ...`
- `Discord bot started (Gateway).`
- `Discord bot logged in as YourBotName#....`

### Health check

In a browser or PowerShell:

```powershell
Invoke-RestMethod http://localhost:5080/health
```

Expect:

- `discordConfigured: true`
- `telegramConfigured: false` (if Telegram token empty)

### Common failures

| Symptom | Fix |
|---|---|
| `Discord bot disabled (DISCORD_BOT_TOKEN not set)` | Token missing/wrong in `.env`; restart after save |
| Login / 401 unauthorized | Token wrong or regenerated; copy a fresh token from Bot page |
| Bot online but never replies | **Message Content Intent** not enabled + Save Changes; restart API |
| Bot replies to others | Set `DISCORD_ALLOWED_USER_IDS` to your user id |
| Can DM but bot silent | Share a server with the bot first, then open DM with the bot |
| In a channel, no reply | You must **@mention** the bot (e.g. `@PersonalAI what's on my calendar?`) |

---

## 9. How to chat with the assistant

### Option A — Direct message (closest to Telegram)

1. In your private server, open the bot’s profile → **Message**.
2. Send: `hello` (no @ needed in DMs).
3. Wait for the reply (same Gemini + Gmail/Calendar tools as the web UI).

### Option B — Server channel with @mention

1. In `#assistant` (or any channel the bot can see), type:  
   `@YourBotName remind me tomorrow at 9am to call mom`
2. The bot only answers when **mentioned** in servers (so it does not spam every message).

Sessions are per Discord user: `discord:<your-user-id>` (separate from web sessions).

---

## 10. Security reminders

- Keep the server private.
- Keep `DISCORD_ALLOWED_USER_IDS` set to **your** id.
- If the token leaks: Developer Portal → Bot → **Reset Token**, update `.env`, restart API.
- Do not commit `.env`.

---

## Optional: turn Discord off

Clear or remove:

```env
DISCORD_BOT_TOKEN=
```

Restart the API. Web chat still works.

---

## Related

- Web UI: http://localhost:3000  
- Gmail/Calendar: [YOU_GMAIL_SETUP.md](./YOU_GMAIL_SETUP.md), [YOU_CALENDAR_SETUP.md](./YOU_CALENDAR_SETUP.md)  
- Old Telegram path (optional): [YOU_STEP_4_TELEGRAM.md](./YOU_STEP_4_TELEGRAM.md)
