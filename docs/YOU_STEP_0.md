# Step 0 — Gemini API key (you)

Do this once before the chat API can call Google Gemini.

## 1. Create an API key

1. Open https://aistudio.google.com/apikey
2. Sign in with Google
3. **Create API key** → copy it (`AIza...`)

This key is **only for the LLM**. It is separate from Gmail/Calendar OAuth (`GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET`).

## 2. Put it in `.env`

At the repo root:

```env
GEMINI_API_KEY=paste_your_key_here
GEMINI_MODEL_ID=gemini-3.5-flash-lite
GEMINI_IMAGE_MODEL_ID=gemini-3.1-flash-image
```

Optional:

```env
# GEMINI_ENDPOINT=https://generativelanguage.googleapis.com/v1beta/openai/
```

### Which model (rate limits first)

This app often uses **several Gemini calls per user message** (Gmail/Calendar tool rounds). Free-tier **RPM** and **RPD** are the usual bottlenecks.

Example free-tier caps from AI Studio **Rate limits** (yours may differ — always check the live page):

| Model | Typical free RPM | Typical free RPD | Fit for this app |
|---|---|---|---|
| **`gemini-3.5-flash-lite`** (default chat) | **15** | **500** | Best: same headroom as 3.1 Lite, newer model |
| `gemini-3.1-flash-lite` | **15** | **500** | Same rate class; fine fallback |
| `gemini-3.6-flash` / `gemini-3-flash` / `gemini-2.5-flash` | **5** | **20** | Too tight for tool loops |
| `gemini-2.5-flash-lite` | **10** | **20** | Weaker daily cap than 3.x Lite |
| `gemini-2.5-flash` | may show RPM | — | Often **404** for new keys (“no longer available to new users”) |

**Rule:** pick the model with the highest usable RPM/RPD that your key can call. Prefer **Flash Lite** over full Flash on free tier.

### Image generation model

Chat/vision uses `GEMINI_MODEL_ID` (**Gemini 3.5 Flash Lite**). Creating images uses a **separate** Nano Banana / Flash Image model from your AI Studio **Rate limits** list:

| AI Studio name | API id (`GEMINI_IMAGE_MODEL_ID`) |
|---|---|
| **Nano Banana 2 (Gemini 3.1 Flash Image)** | **`gemini-3.1-flash-image`** (locked default) |
| Nano Banana 2 Lite (Gemini 3.1 Flash Lite Image) | `gemini-3.1-flash-lite-image` |
| Nano Banana (Gemini 2.5 Flash Preview Image) | `gemini-2.5-flash-preview-image` |
| Nano Banana Pro (Gemini 3 Pro Image) | `gemini-3-pro-image` |

1. Open AI Studio → **Dashboard → Rate limits**
2. Confirm Nano Banana 2 (or a fallback row above) appears
3. Keep `GEMINI_IMAGE_MODEL_ID=gemini-3.1-flash-image` unless generate returns 404 — then try the next row
4. Restart the API

Same `GEMINI_API_KEY` for chat and images. **Imagen 4** and **Veo** on Rate limits use different APIs — this app does **not** call them.

Web: upload PNG/JPEG/WebP/GIF to analyze; ask “generate an image of …” for creation (no confirmation).

## 3. Checklist

- [ ] Key created in AI Studio
- [ ] `.env` has a real `GEMINI_API_KEY`
- [ ] `GEMINI_MODEL_ID=gemini-3.5-flash-lite` (or another high-RPM Lite from your Rate limits page)
- [ ] `GEMINI_IMAGE_MODEL_ID` set to an image model from Rate limits
- [ ] Gmail OAuth still configured separately (see [YOU_GMAIL_SETUP.md](./YOU_GMAIL_SETUP.md))

## First message gets 404

Often means the model is **retired for new API keys** (common for `gemini-2.5-flash`), even if Rate limits still lists it.

**Fix (chat):** set `GEMINI_MODEL_ID=gemini-3.5-flash-lite`, restart the API, try again.

**Fix (image generate):** set `GEMINI_IMAGE_MODEL_ID` to the next fallback from Rate limits (`gemini-3.1-flash-lite-image`, then `gemini-2.5-flash-preview-image`, then `gemini-3-pro-image`).

## First message gets 429

Usually **Google quota**, not the app spamming.

1. Wrong model class — full Flash at **5 RPM / 20 RPD** burns out fast with tools  
2. Peaked RPM — wait ~1 minute  
3. Hit RPD — wait until Pacific midnight reset, or enable billing  

**Fix:** stay on Flash Lite (`gemini-3.5-flash-lite`), restart API, send one test message. Optional: **Set up billing** in AI Studio for higher caps.

Docs: https://ai.google.dev/gemini-api/docs/rate-limits
