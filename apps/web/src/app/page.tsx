"use client";

import { ArrowUp } from "lucide-react";
import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from "react";

type ChatMessage = {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
};

type GmailStatus = {
  configured: boolean;
  connected: boolean;
  email?: string | null;
};

const API_URL =
  process.env.NEXT_PUBLIC_API_URL?.replace(/\/$/, "") || "http://localhost:5080";

export default function HomePage() {
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [gmail, setGmail] = useState<GmailStatus | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: "welcome",
      role: "system",
      content:
        "Personal assistant ready. Connect Gmail, then try: “What are my pending emails?”, “Find emails about invoices”, “What drafts do I have?”, or “Draft an email to …”. Sending requires your confirmation.",
    },
  ]);
  const bottomRef = useRef<HTMLDivElement | null>(null);

  const refreshGmailStatus = useCallback(async () => {
    try {
      const res = await fetch(`${API_URL}/auth/google/status`);
      if (!res.ok) return;
      const data = (await res.json()) as GmailStatus;
      setGmail(data);
    } catch {
      // ignore while API is down
    }
  }, []);

  useEffect(() => {
    void refreshGmailStatus();
    const onFocus = () => void refreshGmailStatus();
    window.addEventListener("focus", onFocus);
    return () => window.removeEventListener("focus", onFocus);
  }, [refreshGmailStatus]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, busy]);

  const canSend = useMemo(() => input.trim().length > 0 && !busy, [input, busy]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const text = input.trim();
    if (!text || busy) return;

    setError(null);
    setBusy(true);
    setInput("");
    setMessages((prev) => [
      ...prev,
      { id: crypto.randomUUID(), role: "user", content: text },
    ]);

    try {
      const response = await fetch(`${API_URL}/v1/chat`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          message: text,
          sessionId: sessionId,
        }),
      });

      const data = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(data.error || `API error (${response.status})`);
      }

      setSessionId(data.sessionId);
      setMessages((prev) => [
        ...prev,
        {
          id: crypto.randomUUID(),
          role: "assistant",
          content: data.reply ?? "(empty reply)",
        },
      ]);
    } catch (err) {
      const message =
        err instanceof Error
          ? err.message
          : "Could not reach the API. Is PersonalAi.Api running on port 5080?";
      setError(message);
      setMessages((prev) => [
        ...prev,
        {
          id: crypto.randomUUID(),
          role: "system",
          content: `Error: ${message}`,
        },
      ]);
    } finally {
      setBusy(false);
    }
  }

  async function disconnectGmail() {
    try {
      await fetch(`${API_URL}/auth/google/disconnect`, { method: "POST" });
      await refreshGmailStatus();
    } catch {
      setError("Could not disconnect Gmail.");
    }
  }

  return (
    <main className="mx-auto flex h-dvh max-w-3xl flex-col px-4 sm:px-6">
      <header className="flex shrink-0 flex-wrap items-end justify-between gap-3 pb-4 pt-6 sm:pt-8">
        <div>
          <h1 className="font-[family-name:var(--font-syne)] text-2xl font-semibold tracking-tight text-ink sm:text-3xl">
            Personal AI Assistant
          </h1>
          <p className="mt-1 text-sm text-muted">Chat · Gmail tools</p>
        </div>
        <div className="flex flex-col items-end gap-2">
          <div className="flex flex-wrap items-center justify-end gap-2">
            {gmail?.connected ? (
              <>
                <span className="rounded-full bg-assistant/80 px-3 py-1 text-xs text-ink">
                  Gmail: {gmail.email || "connected"}
                </span>
                <button
                  type="button"
                  onClick={() => void disconnectGmail()}
                  className="rounded-full border border-border px-3 py-1 text-xs text-muted hover:text-ink"
                >
                  Disconnect
                </button>
              </>
            ) : (
              <a
                href={`${API_URL}/auth/google`}
                target="_blank"
                rel="noreferrer"
                className="rounded-full bg-accent px-3 py-1.5 text-xs font-medium text-accent-ink"
              >
                Connect Gmail
              </a>
            )}
          </div>
          <p className="font-mono text-xs text-muted sm:text-right">
            API: <span className="text-ink/70">{API_URL}</span>
            {sessionId ? (
              <>
                {" "}
                · session <span className="text-ink/70">{sessionId.slice(0, 8)}</span>
              </>
            ) : null}
          </p>
        </div>
      </header>

      <section className="flex min-h-0 flex-1 flex-col" aria-live="polite">
        <div className="flex-1 space-y-4 overflow-y-auto py-2 pr-1">
          {messages.map((message) => (
            <div
              key={message.id}
              className={`animate-message-in max-w-[85%] ${
                message.role === "user"
                  ? "ml-auto"
                  : message.role === "system"
                    ? "mx-auto max-w-md text-center"
                    : "mr-auto"
              }`}
            >
              {message.role === "system" ? (
                <p className="text-sm leading-relaxed text-muted">
                  {message.content}
                </p>
              ) : (
                <div
                  className={`rounded-2xl px-4 py-3 ${
                    message.role === "user"
                      ? "rounded-br-md bg-user text-accent-ink"
                      : "rounded-bl-md bg-assistant/80 text-ink"
                  }`}
                >
                  <span
                    className={`text-[0.65rem] font-medium uppercase tracking-[0.08em] ${
                      message.role === "user"
                        ? "text-accent-ink/60"
                        : "text-muted"
                    }`}
                  >
                    {message.role}
                  </span>
                  <p className="mt-1 whitespace-pre-wrap text-[0.95rem] leading-relaxed">
                    {message.content}
                  </p>
                </div>
              )}
            </div>
          ))}

          {busy ? (
            <div className="mr-auto flex items-center gap-1.5 px-1 py-2">
              <span className="think-dot size-1.5 rounded-full bg-muted" />
              <span className="think-dot size-1.5 rounded-full bg-muted" />
              <span className="think-dot size-1.5 rounded-full bg-muted" />
              <span className="sr-only">Thinking…</span>
            </div>
          ) : null}
          <div ref={bottomRef} />
        </div>

        <div className="shrink-0 border-t border-border/80 bg-transparent pb-5 pt-3 sm:pb-7">
          <form
            className="flex items-end gap-2 rounded-2xl border border-border bg-white/70 p-2 shadow-[0_8px_30px_rgba(15,23,42,0.04)] backdrop-blur-sm"
            onSubmit={onSubmit}
          >
            <textarea
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="Ask about mail, drafts, or anything else…"
              rows={2}
              disabled={busy}
              className="max-h-40 min-h-[48px] flex-1 resize-none bg-transparent px-3 py-2.5 text-[0.95rem] text-ink outline-none placeholder:text-muted/70 disabled:opacity-60"
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  void onSubmit(e);
                }
              }}
            />
            <button
              type="submit"
              disabled={!canSend}
              aria-label="Send"
              className="mb-0.5 flex size-10 shrink-0 items-center justify-center rounded-xl bg-accent text-accent-ink transition-transform duration-150 hover:scale-[1.04] active:scale-95 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:scale-100"
            >
              <ArrowUp className="size-5" strokeWidth={2.25} />
            </button>
          </form>
          {error ? (
            <p className="mt-2 px-1 text-sm text-danger">{error}</p>
          ) : null}
        </div>
      </section>
    </main>
  );
}
