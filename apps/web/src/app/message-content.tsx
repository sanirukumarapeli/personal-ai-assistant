"use client";

import type { Components } from "react-markdown";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

const API_URL =
  process.env.NEXT_PUBLIC_API_URL?.replace(/\/$/, "") || "http://localhost:5080";

const DOC_PATH_TEST =
  /^(\/v1\/documents\/(?:notes|pdfs|office|uploads)\/[^\s)\]"'<>]+)$/i;

const DOC_LINK_RE =
  /(https?:\/\/localhost:5080)?(\/v1\/documents\/(?:notes|pdfs|office|uploads)\/[^\s)\]"'<>]+)/gi;

function fileLabel(path: string): string {
  try {
    const name = decodeURIComponent(path.split("/").pop() || "file");
    return `Download ${name}`;
  } catch {
    return "Download file";
  }
}

function resolveHref(href?: string): string | undefined {
  if (!href) return href;

  // Nested/broken markdown from older preprocess
  const nested = href.match(
    /\]\((https?:\/\/[^)]+|\/v1\/documents\/[^)]+)\)\s*$/i,
  );
  if (nested?.[1]) href = nested[1];

  const pathOnly = href.match(
    /^(?:https?:\/\/localhost:5080)?(\/v1\/documents\/(?:notes|pdfs|office|uploads)\/[^\s)\]"'<>]+)/i,
  );
  if (pathOnly?.[1]) return `http://localhost:5080${pathOnly[1]}`.replace(
    "http://localhost:5080",
    API_URL,
  );

  if (DOC_PATH_TEST.test(href)) return `${API_URL}${href}`;
  if (href.startsWith("http://localhost:5080/")) {
    return href.replace("http://localhost:5080", API_URL);
  }
  // Absolute API URL already
  if (href.includes("/v1/documents/")) {
    try {
      const u = new URL(href, API_URL);
      if (u.pathname.startsWith("/v1/documents/")) {
        return `${API_URL}${u.pathname}${u.search}`;
      }
    } catch {
      // ignore
    }
  }
  return href;
}

/**
 * Linkify bare document paths/URLs. Skip matches already used as a markdown
 * link destination (`](...)`) so we don't nest links and break downloads.
 */
function preprocess(content: string): string {
  return content.replace(DOC_LINK_RE, (match, _host: string | undefined, path: string, offset: number, full: string) => {
    const before = full.slice(Math.max(0, offset - 2), offset);
    if (before === "](") return match;

    // Already a markdown link whose label is the path itself: [path](something)
    // Leave alone if this match is the label portion.
    const after = full.slice(offset + match.length, offset + match.length + 2);
    if (after === "](") return match;

    return `[${fileLabel(path)}](${API_URL}${path})`;
  });
}

function isImageDocHref(href?: string): boolean {
  if (!href) return false;
  try {
    const path = new URL(href, API_URL).pathname;
    return /\.(png|jpe?g|webp|gif)$/i.test(path);
  } catch {
    return /\.(png|jpe?g|webp|gif)$/i.test(href);
  }
}

const components: Components = {
  a({ href, children, ...props }) {
    const resolved = resolveHref(href);
    const isDoc =
      !!resolved &&
      (resolved.includes("/v1/documents/") || resolved.startsWith(API_URL));
    return (
      <a
        href={resolved}
        target={isDoc || resolved?.startsWith("http") ? "_blank" : undefined}
        rel={isDoc || resolved?.startsWith("http") ? "noreferrer" : undefined}
        download={isDoc ? true : undefined}
        {...props}
      >
        {children}
      </a>
    );
  },
  img({ src, alt, ...props }) {
    const resolved = resolveHref(src);
    if (isImageDocHref(resolved) || isImageDocHref(src)) {
      return (
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={resolved || src}
          alt={alt || "Generated image"}
          className="my-2 max-h-96 max-w-full rounded-lg border border-border object-contain"
          loading="lazy"
          {...props}
        />
      );
    }
    return (
      // eslint-disable-next-line @next/next/no-img-element
      <img src={resolved || src} alt={alt || ""} className="max-w-full" {...props} />
    );
  },
};

type MessageContentProps = {
  content: string;
  className?: string;
};

export function MessageContent({ content, className }: MessageContentProps) {
  return (
    <div className={className ? `msg-prose ${className}` : "msg-prose"}>
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>
        {preprocess(content)}
      </ReactMarkdown>
    </div>
  );
}
