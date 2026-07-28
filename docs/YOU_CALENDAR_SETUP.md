# YOU — Google Calendar setup

Calendar uses the **same** Google OAuth app as Gmail.

## 1. Enable Calendar API

1. Open https://console.cloud.google.com/apis/library/calendar-json.googleapis.com  
2. Select project **Personal AI Assistant**  
3. Click **Enable**

## 2. Add Calendar scopes (Data Access)

Google Auth Platform → **Data Access** → **Add or remove scopes** → add:

- `https://www.googleapis.com/auth/calendar.readonly`
- `https://www.googleapis.com/auth/calendar.events`

(Keep your existing Gmail scopes.)

**Save.**

## 3. Reconnect (required)

Old tokens do not include calendar permission.

1. In the web app click **Disconnect**
2. Restart the API
3. Click **Connect Google** again
4. Allow calendar permissions

## 4. What you can do in chat

**Read**
- What’s on tomorrow / this week?
- Find events about “Team sync”
- Am I free Friday 2–3pm?

**Write (confirm required)**
1. Ask to create / move / cancel something  
2. Assistant proposes details  
3. You reply **yes** / **confirm**  
4. It applies the change  

Examples:
- Create a 30-minute meeting 30th July at 10am titled Team sync → then `yes`
- Remind me tomorrow at 9am to call mom → then `yes` (creates a short event + popup at start)
- Move Team sync to 11am → then `confirm`
- Cancel Team sync → then `yes, cancel it`
- Never mind → assistant discards the pending action

Create / update / cancel all need your confirmation (same idea as sending email).
Reminders use Google Calendar popup notifications on your phone/desktop when Calendar notifications are enabled.
