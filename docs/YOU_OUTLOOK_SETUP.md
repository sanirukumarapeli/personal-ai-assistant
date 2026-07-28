# YOU — Outlook / Microsoft Graph setup (personal)

Do this in the browser before we add mail code.  
Do **not** paste secrets into Cursor chat — only into `.env`.

---

## What you will create

| Item | Goes into `.env` as |
|---|---|
| Application (client) ID | `AZURE_CLIENT_ID` |
| Directory (tenant) ID | `AZURE_TENANT_ID` (or use `consumers` for personal-only) |
| Client secret value | `AZURE_CLIENT_SECRET` |
| Redirect URI | must match exactly what we use in code later |

---

## Step A — Open App registrations

1. Sign in at: https://portal.azure.com  
   (Use the **same Microsoft account** that owns the Outlook mailbox you want the assistant to read.)
2. Search for **Microsoft Entra ID** (or **Azure Active Directory**).
3. In the left menu: **App registrations** → **New registration**.

If Azure asks you to create a directory the first time, accept the free default directory.

---

## Step B — Register the app

Fill the form:

| Field | Value |
|---|---|
| **Name** | `Personal AI Assistant` |
| **Supported account types** | **Accounts in any organizational directory and personal Microsoft accounts**  
| | *or* **Personal Microsoft accounts only** (fine if you only use outlook.com/hotmail/live) |
| **Redirect URI** | Platform: **Web**  
| | URI: `http://localhost:5080/signin-oidc` |

Click **Register**.

---

## Step C — Copy IDs

On the app **Overview** page, copy:

1. **Application (client) ID** → `AZURE_CLIENT_ID`
2. **Directory (tenant) ID** → `AZURE_TENANT_ID`

For **personal Microsoft accounts only** apps, you can also set tenant to `consumers` in `.env` later if login fails with the GUID. Start with the GUID from Overview.

Add to project `.env` (repo root):

```env
AZURE_CLIENT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
AZURE_TENANT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

---

## Step D — Create a client secret

1. Left menu: **Certificates & secrets**
2. **Client secrets** → **New client secret**
3. Description: `personal-ai-local`
4. Expiry: 6–12 months (your choice)
5. **Add**
6. Copy the **Value** immediately (shown only once) → `AZURE_CLIENT_SECRET`

```env
AZURE_CLIENT_SECRET=your_secret_value_here
```

If you leave this page without copying, create a new secret.

---

## Step E — API permissions (Microsoft Graph)

1. Left menu: **API permissions**
2. **Add a permission** → **Microsoft Graph** → **Delegated permissions**
3. Add these (search each name):

| Permission | Why |
|---|---|
| `openid` | Sign-in |
| `profile` | Basic profile |
| `offline_access` | Refresh token (stay signed in) |
| `User.Read` | Who am I |
| `Mail.Read` | Read mail |
| `Mail.ReadWrite` | Drafts / manage mail |
| `Mail.Send` | Send mail (we will still require confirm in chat) |
| `Calendars.Read` | Read calendar (useful next) |
| `Calendars.ReadWrite` | Create events later |

4. Click **Add permissions**.

### Consent

- For **personal Microsoft account**: you usually consent when you first sign in (browser popup). You may **not** see “Grant admin consent” — that’s OK.
- If you only see org admin consent and you’re on a work tenant, use a personal account directory or choose “personal Microsoft accounts” supported account type.

---

## Step F — Authentication settings

1. Left menu: **Authentication**
2. Under **Platform configurations** → **Web**, confirm redirect URI:  
   `http://localhost:5080/signin-oidc`
3. Under **Implicit grant and hybrid flows**: leave unchecked (we won’t use implicit).
4. Under **Allow public client flows**: **No** (we use a confidential web/API client with secret).
5. **Save** if you changed anything.

Optional (nice for device/local tools later): add a second redirect  
`http://localhost:5080/signin-oidc` is enough for our first auth flow.

---

## Step G — Final `.env` checklist

Your repo-root `.env` should include (in addition to GitHub Models):

```env
GITHUB_TOKEN=...
GITHUB_MODEL_ID=openai/gpt-5

AZURE_CLIENT_ID=...
AZURE_TENANT_ID=...
AZURE_CLIENT_SECRET=...
```

Save the file. Do not commit real secrets if you push to a public repo.

---

## Step H — Verify you’re ready (before code)

Check all boxes:

- [ ] App registration exists named Personal AI Assistant  
- [ ] Redirect URI `http://localhost:5080/signin-oidc` is listed  
- [ ] Client secret **Value** copied into `.env`  
- [ ] Graph delegated permissions added (Mail.* + offline_access + User.Read)  
- [ ] `.env` has `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`  

When all are done, tell me in chat:

> **Outlook external setup done — implement mail**

Then we’ll add sign-in + Graph mail tools to the .NET API (and wire them into the assistant).

---

## Troubleshooting

| Problem | What to try |
|---|---|
| Can’t open Azure Portal with personal account | Use https://portal.azure.com and create a free Azure account / directory when prompted |
| No “Grant admin consent” | Normal for personal accounts — consent happens at first login |
| Wrong mailbox | Sign in during auth with the Outlook account you want |
| Secret lost | Create a new client secret; update `.env` |
| Redirect mismatch later | Redirect URI in portal must **exactly** match the app (`http` not `https` for local) |

---

## What we will NOT do in this external step

- No company admin approval  
- No Teams publish  
- No sending mail yet (code comes after your setup)  
