"use client";

type PendingConfirmation = {
  kind: string;
  summary: string;
  confirmAction?: string;
  cancelAction?: string;
};

type ConfirmActionsProps = {
  pending: PendingConfirmation;
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
};

export type { PendingConfirmation };

export function ConfirmActions({
  pending,
  busy,
  onConfirm,
  onCancel,
}: ConfirmActionsProps) {
  return (
    <div className="mt-2 flex flex-col gap-2 rounded-2xl border border-border bg-surface/90 p-3">
      <p className="text-xs text-muted">
        Waiting for confirmation
        <span className="text-ink/70"> · {pending.summary}</span>
      </p>
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={busy}
          onClick={onConfirm}
          className="rounded-xl bg-accent px-4 py-2 text-sm font-medium text-accent-ink transition-transform hover:scale-[1.02] active:scale-95 disabled:opacity-40"
        >
          Confirm
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={onCancel}
          className="rounded-xl border border-border px-4 py-2 text-sm text-muted transition-colors hover:text-ink disabled:opacity-40"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
