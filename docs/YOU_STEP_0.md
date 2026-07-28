# YOU — Step 0 external setup

Do this once before the chat API can call GitHub Models.

## 1. Create GitHub PAT

1. Open: https://github.com/settings/tokens?type=beta
2. **Generate new token** (fine-grained)
3. Name it e.g. `personal-ai-assistant`
4. Under **Permissions** → account permissions, set **Models** to **Read**
5. Generate and **copy the token once**

## 2. Put it in `.env`

Open `.env` in the project root and set:

```env
GITHUB_TOKEN=paste_your_token_here
GITHUB_MODEL_ID=openai/gpt-5
```

Do not paste the token into the Cursor chat.

## 3. Confirm tools

```powershell
dotnet --version   # expect 10.x
node -v            # expect v18+ or v20+
```

## 4. Done when

- [ ] PAT created with `models:read`
- [ ] `.env` has a real `GITHUB_TOKEN`
- [ ] `GITHUB_MODEL_ID=openai/gpt-5`
