"use client";

import { LogOut, Moon, Sun } from "lucide-react";
import { useEffect, useRef, useState } from "react";

type Theme = "light" | "dark";

const STORAGE_KEY = "pai-theme";

function getPreferredTheme(): Theme {
  if (typeof window === "undefined") return "light";
  const stored = window.localStorage.getItem(STORAGE_KEY);
  if (stored === "light" || stored === "dark") return stored;
  return window.matchMedia("(prefers-color-scheme: dark)").matches
    ? "dark"
    : "light";
}

function applyTheme(theme: Theme) {
  document.documentElement.classList.toggle("dark", theme === "dark");
}

type AccountMenuProps = {
  connected: boolean;
  email?: string | null;
  connectHref: string;
  onLogout: () => void | Promise<void>;
  /** Narrow rail avatar vs expanded sidebar row */
  compact?: boolean;
};

export function AccountMenu({
  connected,
  email,
  connectHref,
  onLogout,
  compact = false,
}: AccountMenuProps) {
  const [open, setOpen] = useState(false);
  const [theme, setTheme] = useState<Theme>("light");
  const rootRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    setTheme(getPreferredTheme());
  }, []);

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: MouseEvent | PointerEvent) {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }

    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  function toggleAppearance() {
    const next: Theme = theme === "dark" ? "light" : "dark";
    setTheme(next);
    applyTheme(next);
    window.localStorage.setItem(STORAGE_KEY, next);
  }

  async function handleLogout() {
    setOpen(false);
    await onLogout();
  }

  const initial = (email || "G").charAt(0).toUpperCase();

  if (!connected) {
    if (compact) {
      return (
        <a
          href={connectHref}
          target="_blank"
          rel="noreferrer"
          title="Connect Google"
          aria-label="Connect Google"
          className="flex size-8 items-center justify-center rounded-full bg-accent text-xs font-bold text-accent-ink"
        >
          G
        </a>
      );
    }
    return (
      <a
        href={connectHref}
        target="_blank"
        rel="noreferrer"
        className="flex items-center justify-center rounded-xl bg-accent px-3 py-2 text-xs font-semibold text-accent-ink"
      >
        Connect Google
      </a>
    );
  }

  return (
    <div ref={rootRef} className="relative">
      {compact ? (
        <button
          type="button"
          title={email || "Account"}
          aria-label="Account menu"
          aria-expanded={open}
          aria-haspopup="menu"
          onClick={() => setOpen((v) => !v)}
          className="flex size-8 items-center justify-center rounded-full bg-elevated text-xs font-semibold text-ink ring-1 ring-border/60 hover:bg-hover"
        >
          {initial}
        </button>
      ) : (
        <button
          type="button"
          aria-label="Account menu"
          aria-expanded={open}
          aria-haspopup="menu"
          onClick={() => setOpen((v) => !v)}
          className="flex w-full items-center gap-2 rounded-xl px-2 py-2 text-left hover:bg-hover"
        >
          <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-elevated text-xs font-semibold text-ink ring-1 ring-border/60">
            {initial}
          </span>
          <span className="min-w-0 flex-1">
            <span className="block truncate text-xs font-medium text-ink">
              {email || "Google"}
            </span>
            <span className="block text-[0.65rem] text-muted">Connected</span>
          </span>
        </button>
      )}

      {open ? (
        <div
          role="menu"
          className={`absolute z-50 mb-2 w-64 rounded-xl bg-elevated p-1 shadow-[var(--composer-shadow)] ring-1 ring-border ${
            compact ? "bottom-full left-0" : "bottom-full left-0 right-0 w-full min-w-[16rem]"
          }`}
        >
          <div className="truncate px-3 py-2.5 text-xs text-muted">
            {email || "Google account"}
          </div>

          <button
            type="button"
            role="menuitem"
            onClick={toggleAppearance}
            className="flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-sm text-ink hover:bg-hover"
          >
            {theme === "dark" ? (
              <Sun className="size-4 shrink-0 text-muted" strokeWidth={1.75} />
            ) : (
              <Moon className="size-4 shrink-0 text-muted" strokeWidth={1.75} />
            )}
            <span className="flex-1 text-left">Appearance</span>
            <span className="text-xs text-muted">
              {theme === "dark" ? "Dark" : "Light"}
            </span>
          </button>

          <div className="my-1 h-px bg-border/80" />

          <button
            type="button"
            role="menuitem"
            onClick={() => void handleLogout()}
            className="flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-sm text-ink hover:bg-hover"
          >
            <LogOut className="size-4 shrink-0 text-muted" strokeWidth={1.75} />
            Log out
          </button>
        </div>
      ) : null}
    </div>
  );
}
