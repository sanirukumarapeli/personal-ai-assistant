"use client";

import { Check, X } from "lucide-react";
import { useEffect, useRef } from "react";

const POINT_COUNT = 64;

type VoiceWaveProps = {
  active: boolean;
  level: number;
  speaking: boolean;
  capturing: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  canConfirm?: boolean;
};

export function VoiceWave({
  active,
  level,
  speaking,
  capturing,
  onCancel,
  onConfirm,
  canConfirm = false,
}: VoiceWaveProps) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const phaseRef = useRef(0);
  const smoothRef = useRef(0);

  useEffect(() => {
    if (!active) return;
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    let raf = 0;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);

    const resize = () => {
      const rect = canvas.getBoundingClientRect();
      canvas.width = Math.max(1, Math.floor(rect.width * dpr));
      canvas.height = Math.max(1, Math.floor(rect.height * dpr));
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };
    resize();
    window.addEventListener("resize", resize);

    const tick = () => {
      const rect = canvas.getBoundingClientRect();
      const w = rect.width;
      const h = rect.height;
      const mid = h / 2;

      const target =
        speaking || capturing || level > 0.02
          ? Math.min(1, Math.max(level * 1.8, capturing ? 0.45 : 0, speaking ? 0.55 : 0))
          : 0.06;
      smoothRef.current += (target - smoothRef.current) * 0.18;
      const energy = smoothRef.current;
      phaseRef.current += speaking ? 0.16 : capturing || energy > 0.12 ? 0.11 : 0.035;

      ctx.clearRect(0, 0, w, h);

      const isDark =
        typeof document !== "undefined" &&
        document.documentElement.classList.contains("dark");
      const ink = isDark ? "rgba(245, 196, 0, 0.95)" : "rgba(230, 168, 0, 0.95)";
      const muted = isDark ? "rgba(163, 163, 163, 0.55)" : "rgba(115, 115, 115, 0.65)";

      for (let i = 0; i < POINT_COUNT; i++) {
        const x = (i / (POINT_COUNT - 1)) * w;
        const t = phaseRef.current + i * 0.22;
        const envelope = Math.sin((i / (POINT_COUNT - 1)) * Math.PI);
        const wave =
          Math.sin(t * 1.7) * 0.55 +
          Math.sin(t * 3.1 + 1.2) * 0.3 +
          Math.sin(t * 0.9 + i * 0.05) * 0.15;
        const amp = energy * envelope * Math.abs(wave);
        const barH = Math.max(1.5, amp * (h * 0.42));

        if (barH < 3.2) {
          ctx.fillStyle = muted;
          ctx.beginPath();
          ctx.arc(x, mid, 1.15, 0, Math.PI * 2);
          ctx.fill();
        } else {
          ctx.fillStyle = ink;
          const bw = 2.2;
          ctx.beginPath();
          ctx.roundRect(x - bw / 2, mid - barH, bw, barH * 2, bw / 2);
          ctx.fill();
        }
      }

      raf = requestAnimationFrame(tick);
    };

    raf = requestAnimationFrame(tick);
    return () => {
      cancelAnimationFrame(raf);
      window.removeEventListener("resize", resize);
    };
  }, [active, level, speaking, capturing]);

  if (!active) return null;

  const label = speaking ? "Speaking…" : "Listening…";

  return (
    <div
      className="flex min-h-[72px] flex-col gap-2 rounded-[28px] bg-elevated px-4 pb-3 pt-3 shadow-[var(--composer-shadow)]"
      role="status"
      aria-live="polite"
    >
      <div className="flex items-start justify-between gap-3">
        <span className="pt-0.5 text-[0.8rem] text-muted">{label}</span>
        <div className="flex shrink-0 items-center gap-2">
          <button
            type="button"
            aria-label="Cancel voice mode"
            title="Cancel"
            onClick={onCancel}
            className="flex size-9 items-center justify-center rounded-full bg-hover text-muted transition-colors hover:text-ink"
          >
            <X className="size-4" strokeWidth={2.25} />
          </button>
          <button
            type="button"
            aria-label="Send voice message"
            title="Send"
            disabled={!canConfirm}
            onClick={onConfirm}
            className="flex size-9 items-center justify-center rounded-full bg-accent text-accent-ink transition-transform hover:scale-[1.04] active:scale-95 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:scale-100"
          >
            <Check className="size-4" strokeWidth={2.5} />
          </button>
        </div>
      </div>
      <canvas ref={canvasRef} className="h-10 w-full" aria-hidden />
    </div>
  );
}
