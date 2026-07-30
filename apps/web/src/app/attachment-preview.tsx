"use client";

import { FileText } from "lucide-react";

export type AttachmentFile = {
  folder: string;
  name: string;
  kind: string;
  downloadUrl: string;
};

const API_URL =
  process.env.NEXT_PUBLIC_API_URL?.replace(/\/$/, "") || "http://localhost:5080";

export function docHref(downloadUrl: string): string {
  if (/^https?:\/\//i.test(downloadUrl)) return downloadUrl;
  return `${API_URL}${downloadUrl.startsWith("/") ? downloadUrl : `/${downloadUrl}`}`;
}

export function isImageAttachment(file: { name: string; kind?: string }): boolean {
  if (file.kind?.toLowerCase() === "image") return true;
  return /\.(png|jpe?g|webp|gif)$/i.test(file.name);
}

export function badgeLabel(file: { name: string; kind?: string }): string {
  const fromKind = file.kind?.trim();
  if (fromKind && fromKind.toLowerCase() !== "image" && fromKind.length <= 5) {
    return fromKind.toUpperCase();
  }
  const ext = file.name.split(".").pop()?.toUpperCase();
  return ext && ext.length <= 5 ? ext : "FILE";
}

type AttachmentPreviewProps = {
  file: AttachmentFile;
  /** Local object URL for staged image before upload */
  localPreviewUrl?: string | null;
  size?: "composer" | "message";
  uploading?: boolean;
  onRemove?: () => void;
};

export function AttachmentPreview({
  file,
  localPreviewUrl,
  size = "message",
  uploading,
  onRemove,
}: AttachmentPreviewProps) {
  const href = docHref(file.downloadUrl);
  const image = isImageAttachment(file);
  const badge = badgeLabel(file);
  const large = size === "message";

  const cardClass = large
    ? "group relative w-[min(100%,16rem)] overflow-hidden rounded-2xl bg-white shadow-sm ring-1 ring-black/10"
    : `group relative h-[72px] w-[56px] shrink-0 overflow-hidden rounded-lg bg-white shadow-sm ring-1 ring-black/5 ${
        uploading ? "opacity-50" : ""
      }`;

  const inner = image ? (
    // eslint-disable-next-line @next/next/no-img-element
    <img
      src={localPreviewUrl || href}
      alt={file.name}
      className={
        large
          ? "max-h-56 w-full object-cover"
          : "h-full w-full object-cover"
      }
    />
  ) : (
    <div
      className={
        large
          ? "flex min-h-[10rem] flex-col items-center justify-center gap-2 px-4 py-6"
          : "flex h-full flex-col items-center justify-center gap-1 px-1"
      }
    >
      <FileText
        className={large ? "size-10 text-neutral-400" : "size-6 text-neutral-400"}
        strokeWidth={1.5}
      />
      <span
        className={
          large
            ? "line-clamp-2 w-full text-center text-xs leading-snug text-neutral-600"
            : "w-full truncate px-0.5 text-center text-[0.55rem] leading-tight text-neutral-500"
        }
        title={file.name}
      >
        {file.name}
      </span>
    </div>
  );

  const badgeEl = (
    <span
      className={
        large
          ? "absolute bottom-2 left-2 rounded bg-neutral-800 px-1.5 py-0.5 text-[0.65rem] font-semibold uppercase leading-none text-white"
          : "absolute bottom-1 left-1 rounded bg-neutral-800 px-1.5 py-0.5 text-[0.55rem] font-semibold uppercase leading-none text-white"
      }
    >
      {badge}
    </span>
  );

  if (size === "composer") {
    return (
      <div className={cardClass} title={file.name}>
        {inner}
        {badgeEl}
        {onRemove && !uploading ? (
          <button
            type="button"
            aria-label="Remove attachment"
            title="Remove"
            onClick={onRemove}
            className="absolute -right-1.5 -top-1.5 flex size-5 items-center justify-center rounded-full bg-neutral-800 text-white opacity-0 shadow transition-opacity group-hover:opacity-100"
          >
            <XIcon />
          </button>
        ) : null}
      </div>
    );
  }

  return (
    <a
      href={href}
      target="_blank"
      rel="noreferrer"
      className={`${cardClass} block transition-opacity hover:opacity-95`}
      title={file.name}
    >
      {inner}
      {badgeEl}
    </a>
  );
}

function XIcon() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2.5}
      className="size-3"
      aria-hidden
    >
      <path d="M18 6 6 18M6 6l12 12" strokeLinecap="round" />
    </svg>
  );
}
