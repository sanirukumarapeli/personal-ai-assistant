# YOU — Gmail / Google OAuth setup

## Required in project-root `.env`

```env
GOOGLE_CLIENT_ID=xxxx.apps.googleusercontent.com
GOOGLE_CLIENT_SECRET=GOCSPX-xxxx
```

Also keep:

```env
GEMINI_API_KEY=...
GEMINI_MODEL_ID=gemini-2.5-flash
```

## Google Cloud checklist

- [ ] Project created (e.g. Personal AI Assistant)
- [ ] **Gmail API** enabled
- [ ] OAuth client type: **Web application**
- [ ] Redirect URI exactly: `http://localhost:5080/signin-google`
- [ ] Audience: **Testing** + your Gmail as **test user**
- [ ] Data Access scopes include at least:
  - `openid` / `email` / `profile`
  - `https://www.googleapis.com/auth/gmail.readonly`
  - `https://www.googleapis.com/auth/gmail.compose`

Find client secret: https://console.cloud.google.com/apis/credentials → open your Web client → Client secret / Download JSON.

Do not paste secrets into chat.

## How to connect and test (after code is running)

1. Start API: `dotnet run --project src/PersonalAi.Api`
2. Start Web: `cd apps/web && npm run dev`
3. Open http://localhost:3000 → **Connect Gmail** → sign in with test user → Allow
4. Check http://localhost:5080/auth/google/status → `"connected": true`
5. In chat try:
   - `What are my pending emails?`
   - `Find emails about invoices`
   - `What drafts do I have?`
   - `Draft an email to me@example.com about lunch tomorrow`
   - `Yes, send it` (only after a draft was created)

Send only happens after an explicit confirmation in chat.
