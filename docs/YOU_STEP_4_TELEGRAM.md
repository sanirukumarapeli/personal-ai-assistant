# YOU — Step 4 Telegram setup

Do this only after Web chat works.

## 1. Create a bot

1. Open Telegram and chat with `@BotFather`
2. Send `/newbot`
3. Choose a display name and a username ending in `bot`
4. Copy the HTTP API token

## 2. Add to project `.env`

```env
TELEGRAM_BOT_TOKEN=123456:ABC...
```

## 3. Restart the API

```powershell
dotnet run --project src/PersonalAi.Api
```

You should see a log like: `Telegram bot @YourBot started (long polling).`

## 4. Test

Message your bot on your phone. It uses the same Gemini brain as the web app.

## Notes

- Long polling works on your PC (no public URL needed).
- Leave `TELEGRAM_BOT_TOKEN` empty to keep Telegram disabled.
