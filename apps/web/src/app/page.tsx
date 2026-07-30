"use client";

import {
  ArrowUp,
  Check,
  Copy,
  FileText,
  MessageSquare,
  Mic,
  PanelLeft,
  Pencil,
  Plus,
  Square,
  Volume2,
  VolumeX,
  X,
} from "lucide-react";
import {
  FormEvent,
  type ClipboardEvent,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { ConfirmActions, type PendingConfirmation } from "./confirm-actions";
import { AccountMenu } from "./account-menu";
import {
  AttachmentPreview,
  docHref,
} from "./attachment-preview";
import { MessageContent } from "./message-content";
import { ThemeToggle } from "./theme-toggle";
import { VoiceWave } from "./voice-wave";

type ChatMessage = {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  sentAt?: string;
  attachment?: ChatFile;
};

type GmailStatus = {
  configured: boolean;
  connected: boolean;
  email?: string | null;
};

type SessionItem = {
  sessionId: string;
  updatedAtUtc: string;
  preview: string;
};

type SessionTurn = {
  role: string;
  content: string;
  createdAtUtc?: string;
};

const API_URL =
  process.env.NEXT_PUBLIC_API_URL?.replace(/\/$/, "") || "http://localhost:5080";

const SESSION_KEY = "pai-session-id";
const SESSION_FILES_KEY = "pai-session-files";
const PENDING_FILES_KEY = "pending";
const VOICE_PAUSE_MS = 2000;
const MIN_VOICE_CHARS = 3;
const WAKE_RE = /\b(hello|hi)\b/i;

type ChatFile = {
  folder: string;
  name: string;
  kind: string;
  downloadUrl: string;
};

type SessionFilesMap = Record<string, ChatFile[]>;

const ALLOWED_UPLOAD_EXT = new Set([
  ".pdf",
  ".docx",
  ".xlsx",
  ".pptx",
  ".png",
  ".jpg",
  ".jpeg",
  ".webp",
  ".gif",
]);

function isAllowedUploadFile(file: File): boolean {
  const ext = `.${(file.name.split(".").pop() || "").toLowerCase()}`;
  if (ALLOWED_UPLOAD_EXT.has(ext)) return true;
  // Screenshot paste often has empty or generic name but image MIME
  if (file.type.startsWith("image/")) {
    const fromMime = file.type.replace("image/", ".");
    if (fromMime === ".jpeg" || fromMime === ".jpg" || fromMime === ".png" || fromMime === ".webp" || fromMime === ".gif")
      return true;
    if (file.type === "image/png" || file.type === "image/jpeg" || file.type === "image/webp" || file.type === "image/gif")
      return true;
  }
  return false;
}

function normalizePastedFile(file: File): File {
  if (file.name && file.name.includes(".")) return file;
  const ext =
    file.type === "image/jpeg"
      ? ".jpg"
      : file.type === "image/webp"
        ? ".webp"
        : file.type === "image/gif"
          ? ".gif"
          : file.type.startsWith("image/")
            ? ".png"
            : "";
  if (!ext) return file;
  return new File([file], `pasted-image${ext}`, { type: file.type || "image/png" });
}

function attachmentFromContent(
  content: string,
  files: ChatFile[],
): ChatFile | undefined {
  if (!files.length) return undefined;
  const analyze = content.match(/Analyze this image:\s*(.+)$/i);
  const summarize = content.match(/^Summarize\s+(.+)$/i);
  const candidate = (analyze?.[1] || summarize?.[1] || "").trim();
  if (candidate) {
    const exact = files.find((f) => f.name === candidate);
    if (exact) return exact;
  }
  for (const f of files) {
    if (content.includes(f.name)) return f;
  }
  return undefined;
}

function readSessionFilesMap(): SessionFilesMap {
  if (typeof window === "undefined") return {};
  try {
    const raw = window.localStorage.getItem(SESSION_FILES_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw) as SessionFilesMap;
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch {
    return {};
  }
}

function writeSessionFilesMap(map: SessionFilesMap) {
  window.localStorage.setItem(SESSION_FILES_KEY, JSON.stringify(map));
}

function fileKey(f: ChatFile): string {
  return `${f.folder}/${f.name}`;
}

function kindFromFileName(name: string): string {
  const ext = name.split(".").pop()?.toUpperCase();
  return ext && ext.length <= 5 ? ext : "FILE";
}

function formatTime(iso?: string): string {
  if (!iso) return "";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function stripWakeWords(text: string): string {
  return text
    .replace(/^\s*(hello|hi)([,!.]?\s+|$)/i, "")
    .replace(/\b(hello|hi)\b[,!.]?\s*/gi, " ")
    .replace(/\s+/g, " ")
    .trim();
}

type SpeechRecognitionLike = {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  start: () => void;
  stop: () => void;
  abort?: () => void;
  onresult:
    | ((event: {
        resultIndex: number;
        results: ArrayLike<{
          isFinal?: boolean;
          0: { transcript: string };
        }>;
      }) => void)
    | null;
  onerror: ((event?: { error?: string }) => void) | null;
  onend: (() => void) | null;
};

function getSpeechRecognitionCtor(): (new () => SpeechRecognitionLike) | null {
  if (typeof window === "undefined") return null;
  const w = window as unknown as {
    SpeechRecognition?: new () => SpeechRecognitionLike;
    webkitSpeechRecognition?: new () => SpeechRecognitionLike;
  };
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null;
}

function countPersistedTurnsBefore(
  messages: ChatMessage[],
  targetId: string,
): number {
  let count = 0;
  for (const m of messages) {
    if (m.id === targetId) break;
    if (m.role === "user" || m.role === "assistant") count += 1;
  }
  return count;
}

export default function HomePage() {
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [gmail, setGmail] = useState<GmailStatus | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [sessions, setSessions] = useState<SessionItem[]>([]);
  const [sessionFilesMap, setSessionFilesMap] = useState<SessionFilesMap>({});
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [docsOpen, setDocsOpen] = useState(false);
  const [voiceMode, setVoiceMode] = useState(false);
  const [speechSupported, setSpeechSupported] = useState(false);
  const [wakeArmed, setWakeArmed] = useState(false);
  const [micLevel, setMicLevel] = useState(0);
  const [capturing, setCapturing] = useState(false);
  const [ttsSpeaking, setTtsSpeaking] = useState(false);
  const [speakingMessageId, setSpeakingMessageId] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editDraft, setEditDraft] = useState("");
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [pendingConfirmation, setPendingConfirmation] =
    useState<PendingConfirmation | null>(null);
  const [stagedFile, setStagedFile] = useState<File | null>(null);

  const bottomRef = useRef<HTMLDivElement | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const recognitionRef = useRef<SpeechRecognitionLike | null>(null);
  const wantRecognitionRef = useRef(false);
  const voiceModeRef = useRef(false);
  const busyRef = useRef(false);
  const draftRef = useRef("");
  const finalBufferRef = useRef("");
  const pauseTimerRef = useRef<number | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  const sendMessageRef = useRef<
    (text: string, opts?: { fromVoice?: boolean; attachment?: ChatFile }) => Promise<void>
  >(async () => {});
  const [stagedPreviewUrl, setStagedPreviewUrl] = useState<string | null>(null);
  const audioCtxRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const mediaStreamRef = useRef<MediaStream | null>(null);
  const levelRafRef = useRef(0);

  const refreshGmailStatus = useCallback(async () => {
    try {
      const res = await fetch(`${API_URL}/auth/google/status`);
      if (!res.ok) return;
      setGmail((await res.json()) as GmailStatus);
    } catch {
      // ignore
    }
  }, []);

  const refreshSessions = useCallback(async () => {
    try {
      const res = await fetch(`${API_URL}/v1/sessions`);
      if (!res.ok) return;
      const data = await res.json();
      setSessions((data.sessions as SessionItem[]) ?? []);
    } catch {
      // ignore
    }
  }, []);

  const updateSessionFilesMap = useCallback((updater: (prev: SessionFilesMap) => SessionFilesMap) => {
    setSessionFilesMap((prev) => {
      const next = updater(prev);
      writeSessionFilesMap(next);
      return next;
    });
  }, []);

  const attachFileToSession = useCallback(
    (file: ChatFile, targetSessionId: string | null) => {
      const key = targetSessionId || PENDING_FILES_KEY;
      updateSessionFilesMap((prev) => {
        const existing = prev[key] ?? [];
        if (existing.some((f) => fileKey(f) === fileKey(file))) return prev;
        return { ...prev, [key]: [...existing, file] };
      });
    },
    [updateSessionFilesMap],
  );

  useEffect(() => {
    const stored = window.localStorage.getItem(SESSION_KEY);
    if (stored) setSessionId(stored);
    setSessionFilesMap(readSessionFilesMap());
    setSpeechSupported(!!getSpeechRecognitionCtor());
    void refreshGmailStatus();
    void refreshSessions();
    const onFocus = () => {
      void refreshGmailStatus();
      void refreshSessions();
    };
    window.addEventListener("focus", onFocus);
    return () => window.removeEventListener("focus", onFocus);
  }, [refreshGmailStatus, refreshSessions]);

  useEffect(() => {
    if (sessionId) window.localStorage.setItem(SESSION_KEY, sessionId);
    else window.localStorage.removeItem(SESSION_KEY);
    sessionIdRef.current = sessionId;
  }, [sessionId]);

  // Merge pending uploads into the real session once it exists
  useEffect(() => {
    if (!sessionId) return;
    updateSessionFilesMap((prev) => {
      const pending = prev[PENDING_FILES_KEY];
      if (!pending?.length) return prev;
      const existing = prev[sessionId] ?? [];
      const merged = [...existing];
      for (const f of pending) {
        if (!merged.some((x) => fileKey(x) === fileKey(f))) merged.push(f);
      }
      const next = { ...prev, [sessionId]: merged };
      delete next[PENDING_FILES_KEY];
      return next;
    });
  }, [sessionId, updateSessionFilesMap]);

  // Close files panel when current chat has no files
  useEffect(() => {
    const key = sessionId || PENDING_FILES_KEY;
    const files = sessionFilesMap[key] ?? [];
    if (files.length === 0 && docsOpen) setDocsOpen(false);
  }, [sessionId, sessionFilesMap, docsOpen]);

  useEffect(() => {
    voiceModeRef.current = voiceMode;
  }, [voiceMode]);

  useEffect(() => {
    busyRef.current = busy;
  }, [busy]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, busy, uploading]);

  const canSend = useMemo(
    () =>
      (input.trim().length > 0 || stagedFile != null) && !busy && !uploading,
    [input, stagedFile, busy, uploading],
  );

  const stagedKind = useMemo(
    () => (stagedFile ? kindFromFileName(stagedFile.name) : null),
    [stagedFile],
  );

  useEffect(() => {
    if (!stagedFile || !stagedFile.type.startsWith("image/")) {
      setStagedPreviewUrl((prev) => {
        if (prev) URL.revokeObjectURL(prev);
        return null;
      });
      return;
    }
    const url = URL.createObjectURL(stagedFile);
    setStagedPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return url;
    });
    return () => URL.revokeObjectURL(url);
  }, [stagedFile]);

  const chatTitle = useMemo(() => {
    if (!sessionId) return "New chat";
    const match = sessions.find((s) => s.sessionId === sessionId);
    return match?.preview?.trim() || "Chat";
  }, [sessionId, sessions]);

  const chatFiles = useMemo(() => {
    const key = sessionId || PENDING_FILES_KEY;
    return sessionFilesMap[key] ?? [];
  }, [sessionId, sessionFilesMap]);

  function clearPauseTimer() {
    if (pauseTimerRef.current != null) {
      window.clearTimeout(pauseTimerRef.current);
      pauseTimerRef.current = null;
    }
  }

  function stopSpeech() {
    if (typeof window !== "undefined" && "speechSynthesis" in window) {
      window.speechSynthesis.cancel();
    }
    setTtsSpeaking(false);
    setSpeakingMessageId(null);
  }

  function speakText(text: string, force = false, messageId?: string) {
    if (!force && !voiceModeRef.current) return;
    if (typeof window === "undefined" || !("speechSynthesis" in window)) return;
    stopSpeech();
    const utter = new SpeechSynthesisUtterance(text.slice(0, 1200));
    utter.rate = 1;
    utter.onstart = () => {
      setTtsSpeaking(true);
      if (messageId) setSpeakingMessageId(messageId);
    };
    utter.onend = () => {
      setTtsSpeaking(false);
      setSpeakingMessageId(null);
    };
    utter.onerror = () => {
      setTtsSpeaking(false);
      setSpeakingMessageId(null);
    };
    setTtsSpeaking(true);
    if (messageId) setSpeakingMessageId(messageId);
    window.speechSynthesis.speak(utter);
  }

  function toggleSpeakMessage(message: ChatMessage) {
    if (ttsSpeaking && speakingMessageId === message.id) {
      stopSpeech();
      return;
    }
    speakText(message.content, true, message.id);
  }

  function stopMicAnalyser() {
    if (levelRafRef.current) cancelAnimationFrame(levelRafRef.current);
    levelRafRef.current = 0;
    mediaStreamRef.current?.getTracks().forEach((t) => t.stop());
    mediaStreamRef.current = null;
    void audioCtxRef.current?.close().catch(() => {});
    audioCtxRef.current = null;
    analyserRef.current = null;
    setMicLevel(0);
  }

  async function startMicAnalyser() {
    stopMicAnalyser();
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      mediaStreamRef.current = stream;
      const ctx = new AudioContext();
      audioCtxRef.current = ctx;
      const source = ctx.createMediaStreamSource(stream);
      const analyser = ctx.createAnalyser();
      analyser.fftSize = 256;
      source.connect(analyser);
      analyserRef.current = analyser;
      const data = new Uint8Array(analyser.frequencyBinCount);
      const tick = () => {
        analyser.getByteFrequencyData(data);
        let sum = 0;
        for (let i = 0; i < data.length; i++) sum += data[i];
        const avg = sum / (data.length * 255);
        setMicLevel(avg);
        levelRafRef.current = requestAnimationFrame(tick);
      };
      tick();
    } catch {
      // wave falls back to capturing pulse
    }
  }

  const stopRecognition = useCallback(() => {
    wantRecognitionRef.current = false;
    try {
      recognitionRef.current?.abort?.();
      recognitionRef.current?.stop();
    } catch {
      // ignore
    }
    recognitionRef.current = null;
    setCapturing(false);
  }, []);

  const scheduleVoiceSend = useCallback(() => {
    clearPauseTimer();
    pauseTimerRef.current = window.setTimeout(() => {
      pauseTimerRef.current = null;
      if (!voiceModeRef.current || busyRef.current) return;
      const text = draftRef.current.trim();
      if (text.length < MIN_VOICE_CHARS) return;
      draftRef.current = "";
      setInput("");
      setCapturing(false);
      finalBufferRef.current = "";
      void sendMessageRef.current(text, { fromVoice: true });
    }, VOICE_PAUSE_MS);
  }, []);

  const handleRecognitionResult = useCallback(
    (transcript: string, isFinal: boolean) => {
      const raw = transcript.trim();
      if (!raw) return;

      if (!voiceModeRef.current) {
        if (!WAKE_RE.test(raw)) return;
        const remainder = stripWakeWords(raw);
        setVoiceMode(true);
        voiceModeRef.current = true;
        void startMicAnalyser();
        if (remainder.length >= MIN_VOICE_CHARS) {
          draftRef.current = remainder;
          setInput(remainder);
          if (isFinal) scheduleVoiceSend();
        } else {
          draftRef.current = "";
          setInput("");
        }
        setCapturing(true);
        return;
      }

      // Voice mode: ignore lone wake words
      const cleaned = stripWakeWords(raw);
      const text = cleaned.length > 0 ? cleaned : raw;
      if (WAKE_RE.test(raw) && stripWakeWords(raw).length === 0) return;

      draftRef.current = text;
      setInput(text);
      setCapturing(true);
      scheduleVoiceSend();
    },
    [scheduleVoiceSend],
  );

  const startRecognition = useCallback(() => {
    const Ctor = getSpeechRecognitionCtor();
    if (!Ctor) return;

    wantRecognitionRef.current = true;
    try {
      recognitionRef.current?.abort?.();
      recognitionRef.current?.stop();
    } catch {
      // ignore
    }

    const recognition = new Ctor();
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.lang = navigator.language || "en-US";
    recognition.onresult = (event) => {
      let interim = "";
      for (let i = event.resultIndex; i < event.results.length; i++) {
        const result = event.results[i];
        const piece = result?.[0]?.transcript ?? "";
        if (result.isFinal) {
          finalBufferRef.current = `${finalBufferRef.current} ${piece}`.trim();
        } else {
          interim += piece;
        }
      }
      const text = `${finalBufferRef.current} ${interim}`.trim();
      if (text) handleRecognitionResult(text, interim.length === 0);
    };
    recognition.onerror = (event) => {
      if (event?.error === "not-allowed") {
        setError("Microphone permission denied. Allow mic access for voice mode.");
        wantRecognitionRef.current = false;
      }
    };
    recognition.onend = () => {
      recognitionRef.current = null;
      if (wantRecognitionRef.current && document.visibilityState === "visible") {
        window.setTimeout(() => {
          if (wantRecognitionRef.current) startRecognition();
        }, 250);
      }
    };
    recognitionRef.current = recognition;
    try {
      recognition.start();
      setWakeArmed(true);
      setError(null);
    } catch {
      setError("Could not start the microphone.");
    }
  }, [handleRecognitionResult]);

  useEffect(() => {
    return () => {
      clearPauseTimer();
      stopRecognition();
      stopMicAnalyser();
      stopSpeech();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!voiceMode) {
      stopMicAnalyser();
      clearPauseTimer();
      setCapturing(false);
    } else {
      void startMicAnalyser();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [voiceMode]);

  function exitVoiceMode() {
    setVoiceMode(false);
    voiceModeRef.current = false;
    clearPauseTimer();
    draftRef.current = "";
    finalBufferRef.current = "";
    stopSpeech();
    stopMicAnalyser();
    setCapturing(false);
    setInput("");
    if (!wantRecognitionRef.current) startRecognition();
  }

  function confirmVoiceSend() {
    clearPauseTimer();
    if (busyRef.current) return;
    const text = (draftRef.current || input).trim();
    if (text.length < MIN_VOICE_CHARS) return;
    draftRef.current = "";
    finalBufferRef.current = "";
    setInput("");
    setCapturing(false);
    void sendMessageRef.current(text, { fromVoice: true });
  }

  function toggleVoiceMode() {
    const Ctor = getSpeechRecognitionCtor();
    if (!Ctor) {
      setError("Speech recognition is not supported in this browser. Try Chrome or Edge.");
      return;
    }
    if (voiceMode) {
      exitVoiceMode();
      return;
    }
    setVoiceMode(true);
    voiceModeRef.current = true;
    finalBufferRef.current = "";
    setError(null);
    void startMicAnalyser();
    if (!wantRecognitionRef.current) startRecognition();
  }

  function armWakeListening() {
    if (!speechSupported || wantRecognitionRef.current) return;
    startRecognition();
  }

  function stopGeneration() {
    abortRef.current?.abort();
    stopSpeech();
    clearPauseTimer();
  }

  function startNewChat() {
    stopGeneration();
    setSessionId(null);
    setMessages([]);
    setError(null);
    setEditingId(null);
    setPendingConfirmation(null);
    setDocsOpen(false);
    setStagedFile(null);
    setStagedPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  async function loadSession(id: string) {
    if (busy) return;
    setError(null);
    setEditingId(null);
    setPendingConfirmation(null);
    setDocsOpen(false);
    try {
      const res = await fetch(`${API_URL}/v1/sessions/${encodeURIComponent(id)}`);
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.error || `Could not load session (${res.status})`);
      const turns = (data.turns as SessionTurn[]) ?? [];
      setSessionId(id);
      const files = sessionFilesMap[id] ?? [];
      setMessages(
        turns.map((t) => {
          const role = (t.role === "assistant" ? "assistant" : "user") as
            | "user"
            | "assistant";
          const content = t.content;
          return {
            id: crypto.randomUUID(),
            role,
            content,
            sentAt: t.createdAtUtc,
            attachment:
              role === "user" ? attachmentFromContent(content, files) : undefined,
          };
        }),
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load session.");
    }
  }

  async function deleteSession(id: string) {
    try {
      await fetch(`${API_URL}/v1/sessions/${encodeURIComponent(id)}`, {
        method: "DELETE",
      });
      updateSessionFilesMap((prev) => {
        const next = { ...prev };
        delete next[id];
        return next;
      });
      if (sessionId === id) startNewChat();
      await refreshSessions();
    } catch {
      setError("Could not delete session.");
    }
  }

  async function sendMessage(
    text: string,
    opts?: { fromVoice?: boolean; attachment?: ChatFile },
  ) {
    const trimmed = text.trim();
    if (!trimmed || busyRef.current || uploading) return;

    const fromVoice = opts?.fromVoice === true;
    if (!fromVoice) {
      // Typed send: leave voice auto-TTS path
      stopSpeech();
    }

    clearPauseTimer();
    setError(null);
    setBusy(true);
    busyRef.current = true;
    setInput("");
    draftRef.current = "";
    const sentAt = new Date().toISOString();
    setMessages((prev) => [
      ...prev,
      {
        id: crypto.randomUUID(),
        role: "user",
        content: trimmed,
        sentAt,
        attachment: opts?.attachment,
      },
    ]);

    const controller = new AbortController();
    abortRef.current = controller;
    const timeout = window.setTimeout(() => controller.abort("timeout"), 90_000);

    try {
      const response = await fetch(`${API_URL}/v1/chat`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message: trimmed, sessionId: sessionIdRef.current }),
        signal: controller.signal,
      });

      const data = await response.json().catch(() => ({}));
      if (response.status === 499 || data.cancelled) {
        setMessages((prev) => [
          ...prev,
          { id: crypto.randomUUID(), role: "system", content: "Stopped." },
        ]);
        return;
      }
      if (!response.ok) {
        throw new Error(data.error || `API error (${response.status})`);
      }

      setSessionId(data.sessionId);
      const reply = data.reply ?? "(empty reply)";
      const replyAt = new Date().toISOString();
      setMessages((prev) => [
        ...prev,
        {
          id: crypto.randomUUID(),
          role: "assistant",
          content: reply,
          sentAt: replyAt,
        },
      ]);
      setPendingConfirmation(
        (data.pendingConfirmation as PendingConfirmation | null | undefined) ?? null,
      );
      if (fromVoice || voiceModeRef.current) {
        speakText(reply, true);
      }
      void refreshSessions();
    } catch (err) {
      if (err instanceof DOMException && err.name === "AbortError") {
        const timedOut = controller.signal.reason === "timeout";
        setMessages((prev) => [
          ...prev,
          {
            id: crypto.randomUUID(),
            role: "system",
            content: timedOut
              ? "Request timed out. Gemini may be rate-limited — wait a minute and try again."
              : "Stopped.",
          },
        ]);
        if (timedOut) {
          setError(
            "Request timed out. Gemini may be rate-limited — wait a minute and try again.",
          );
        }
        return;
      }
      const message =
        err instanceof Error
          ? err.message
          : "Could not reach the API. Is PersonalAi.Api running on port 5080?";
      setError(message);
      setMessages((prev) => [
        ...prev,
        { id: crypto.randomUUID(), role: "system", content: `Error: ${message}` },
      ]);
    } finally {
      window.clearTimeout(timeout);
      abortRef.current = null;
      setBusy(false);
      busyRef.current = false;
    }
  }

  sendMessageRef.current = sendMessage;

  async function resolvePending(action: "confirm" | "cancel") {
    const id = sessionIdRef.current;
    if (!id || busyRef.current) return;

    setBusy(true);
    busyRef.current = true;
    setError(null);
    try {
      const res = await fetch(
        `${API_URL}/v1/sessions/${encodeURIComponent(id)}/${action}`,
        { method: "POST" },
      );
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.error || `${action} failed (${res.status})`);

      const reply = data.reply ?? (action === "confirm" ? "Confirmed." : "Cancelled.");
      const sentAt = new Date().toISOString();
      setMessages((prev) => [
        ...prev,
        {
          id: crypto.randomUUID(),
          role: "user",
          content: action === "confirm" ? "Confirm" : "Cancel",
          sentAt,
        },
        {
          id: crypto.randomUUID(),
          role: "assistant",
          content: reply,
          sentAt,
        },
      ]);
      setPendingConfirmation(
        (data.pendingConfirmation as PendingConfirmation | null | undefined) ?? null,
      );
      if (voiceModeRef.current) speakText(reply, true);
      void refreshSessions();
    } catch (err) {
      setError(err instanceof Error ? err.message : `Could not ${action}.`);
    } finally {
      setBusy(false);
      busyRef.current = false;
    }
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (busy || uploading) return;

    let text = input.trim();
    const file = stagedFile;
    let uploaded: ChatFile | undefined;

    if (file) {
      setError(null);
      setUploading(true);
      try {
        uploaded = await uploadStagedFile(file);
        setStagedFile(null);
        if (fileInputRef.current) fileInputRef.current.value = "";
        if (!text) {
          const isImage = /\.(png|jpe?g|webp|gif)$/i.test(uploaded.name);
          text = isImage
            ? `Analyze this image: ${uploaded.name}`
            : `Summarize ${uploaded.name}`;
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : "Upload failed.");
        setUploading(false);
        return;
      } finally {
        setUploading(false);
      }
    }

    if (!text) return;
    await sendMessage(text, { fromVoice: false, attachment: uploaded });
  }

  function stageFile(file: File) {
    const normalized = normalizePastedFile(file);
    if (!isAllowedUploadFile(normalized)) {
      setError("Only PDF, Word, Excel, PowerPoint, or images (png/jpg/webp/gif) can be attached.");
      return;
    }
    setError(null);
    setStagedFile(normalized);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  function onComposerPaste(e: ClipboardEvent) {
    const items = e.clipboardData?.items;
    const files = e.clipboardData?.files;
    let file: File | null = null;

    if (files && files.length > 0) {
      file = files[0] ?? null;
    } else if (items) {
      for (let i = 0; i < items.length; i++) {
        const item = items[i];
        if (item.kind === "file") {
          file = item.getAsFile();
          if (file) break;
        }
      }
    }

    if (!file) return;
    e.preventDefault();
    stageFile(file);
  }

  async function uploadStagedFile(file: File): Promise<ChatFile> {
    const form = new FormData();
    form.append("file", file);
    const response = await fetch(`${API_URL}/v1/documents/upload`, {
      method: "POST",
      body: form,
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(data.error || `Upload failed (${response.status})`);
    }
    const name = data.name as string;
    const downloadUrl = data.downloadUrl as string;
    const folder = (data.folder as string) || "uploads";
    const kind = (data.kind as string) || kindFromFileName(name);
    const chatFile: ChatFile = { folder, name, kind, downloadUrl };
    attachFileToSession(chatFile, sessionIdRef.current);
    return chatFile;
  }

  async function copyMessage(message: ChatMessage) {
    try {
      await navigator.clipboard.writeText(message.content);
      setCopiedId(message.id);
      window.setTimeout(() => setCopiedId((id) => (id === message.id ? null : id)), 1500);
    } catch {
      setError("Could not copy to clipboard.");
    }
  }

  function startEdit(message: ChatMessage) {
    if (busy || message.role !== "user") return;
    setEditingId(message.id);
    setEditDraft(message.content);
  }

  async function saveEdit(messageId: string) {
    const text = editDraft.trim();
    if (!text || busy) return;

    const keepCount = countPersistedTurnsBefore(messages, messageId);
    const idx = messages.findIndex((m) => m.id === messageId);
    const keptUi = messages.slice(0, idx);

    if (sessionId) {
      try {
        const res = await fetch(
          `${API_URL}/v1/sessions/${encodeURIComponent(sessionId)}/truncate`,
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ keepCount }),
          },
        );
        if (!res.ok) {
          const data = await res.json().catch(() => ({}));
          throw new Error(data.error || `Truncate failed (${res.status})`);
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : "Could not edit message.");
        return;
      }
    }

    setEditingId(null);
    setMessages(keptUi);
    await sendMessage(text, { fromVoice: false });
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
    <div className="flex h-dvh bg-canvas" onPointerDown={armWakeListening}>
      <aside
        className={`shrink-0 border-r border-border/40 bg-sidebar transition-[width] duration-200 ${
          sidebarOpen ? "w-[260px]" : "w-[52px]"
        }`}
      >
        {sidebarOpen ? (
          <div className="flex h-full w-[260px] flex-col px-3 py-3">
            <div className="mb-3 flex items-center justify-between gap-2 px-1">
              <span className="font-[family-name:var(--font-syne)] text-[1.05rem] font-semibold tracking-tight text-ink">
                Midas
              </span>
              <button
                type="button"
                aria-label="Collapse sidebar"
                onClick={() => setSidebarOpen(false)}
                className="flex size-8 items-center justify-center rounded-lg text-muted hover:bg-hover hover:text-ink"
              >
                <PanelLeft className="size-4" />
              </button>
            </div>

            <button
              type="button"
              onClick={startNewChat}
              className="mb-4 flex w-full items-center gap-2 rounded-xl px-3 py-2.5 text-sm font-medium text-ink transition-colors hover:bg-hover"
            >
              <Plus className="size-4 text-accent" strokeWidth={2.25} />
              New chat
            </button>

            <div className="mb-2 px-3 text-[0.7rem] font-medium uppercase tracking-[0.08em] text-muted">
              Recents
            </div>
            <div className="flex-1 space-y-0.5 overflow-y-auto pb-2">
              {sessions.length === 0 ? (
                <p className="px-3 py-2 text-xs text-muted">No chats yet</p>
              ) : (
                sessions.map((s) => {
                  const active = sessionId === s.sessionId;
                  return (
                    <div
                      key={s.sessionId}
                      className={`group relative flex items-center gap-1 rounded-xl ${
                        active ? "bg-active-row" : "hover:bg-hover"
                      }`}
                    >
                      {active ? (
                        <span className="absolute left-0 top-1/2 h-5 w-[3px] -translate-y-1/2 rounded-full bg-accent" />
                      ) : null}
                      <button
                        type="button"
                        className="min-w-0 flex-1 truncate px-3 py-2.5 text-left text-sm text-ink"
                        onClick={() => void loadSession(s.sessionId)}
                      >
                        {s.preview || "Untitled chat"}
                      </button>
                      <button
                        type="button"
                        aria-label="Delete chat"
                        className="mr-1 rounded-md p-1 text-muted opacity-0 hover:text-danger group-hover:opacity-100"
                        onClick={() => void deleteSession(s.sessionId)}
                      >
                        <X className="size-3.5" />
                      </button>
                    </div>
                  );
                })
              )}
            </div>

            <div className="mt-auto space-y-2 border-t border-border/60 pt-3">
              <AccountMenu
                connected={!!gmail?.connected}
                email={gmail?.email}
                connectHref={`${API_URL}/auth/google`}
                onLogout={disconnectGmail}
              />
            </div>
          </div>
        ) : (
          <div className="flex h-full w-[52px] flex-col items-center py-3">
            <button
              type="button"
              aria-label="Expand sidebar"
              title="Expand"
              onClick={() => setSidebarOpen(true)}
              className="mb-3 flex size-9 items-center justify-center rounded-lg text-muted hover:bg-hover hover:text-ink"
            >
              <PanelLeft className="size-4" />
            </button>

            <div className="flex flex-col items-center gap-1">
              <button
                type="button"
                aria-label="New chat"
                title="New chat"
                onClick={startNewChat}
                className="flex size-9 items-center justify-center rounded-full bg-elevated text-ink shadow-sm ring-1 ring-border/60 hover:bg-hover"
              >
                <Plus className="size-4" strokeWidth={2.25} />
              </button>
              <button
                type="button"
                aria-label="Chats"
                title="Chats"
                onClick={() => setSidebarOpen(true)}
                className="flex size-9 items-center justify-center rounded-lg text-muted hover:bg-hover hover:text-ink"
              >
                <MessageSquare className="size-4" strokeWidth={1.75} />
              </button>
              {chatFiles.length > 0 ? (
                <button
                  type="button"
                  aria-label="Files"
                  title="Files"
                  onClick={() => setDocsOpen(true)}
                  className={`flex size-9 items-center justify-center rounded-lg hover:bg-hover hover:text-ink ${
                    docsOpen ? "bg-hover text-ink" : "text-muted"
                  }`}
                >
                  <FileText className="size-4" strokeWidth={1.75} />
                </button>
              ) : null}
            </div>

            <div className="mt-auto flex flex-col items-center pb-1">
              <AccountMenu
                compact
                connected={!!gmail?.connected}
                email={gmail?.email}
                connectHref={`${API_URL}/auth/google`}
                onLogout={disconnectGmail}
              />
            </div>
          </div>
        )}
      </aside>

      <div className="flex min-w-0 flex-1">
      <main className="relative flex min-w-0 flex-1 flex-col bg-canvas">
        <header className="flex shrink-0 items-center gap-2 px-4 pb-2 pt-4 sm:px-6">
          <h1 className="min-w-0 flex-1 truncate text-sm font-medium text-ink">
            {chatTitle}
          </h1>
          {voiceMode ? (
            <span className="shrink-0 text-xs text-accent">Voice</span>
          ) : wakeArmed ? (
            <span className="shrink-0 text-xs text-muted">Listening</span>
          ) : null}
          <div className="ml-auto flex shrink-0 items-center gap-1">
            <ThemeToggle />
            {chatFiles.length > 0 ? (
              <button
                type="button"
                onClick={() => setDocsOpen((v) => !v)}
                className={`flex size-8 items-center justify-center rounded-lg transition-colors hover:bg-hover hover:text-ink ${
                  docsOpen ? "bg-hover text-ink" : "text-muted"
                }`}
                aria-label="Files"
                title="Files"
                aria-pressed={docsOpen}
              >
                <FileText className="size-4" />
              </button>
            ) : null}
          </div>
        </header>

        <section className="flex min-h-0 flex-1 flex-col" aria-live="polite">
          <div className="min-h-0 w-full flex-1 overflow-y-auto">
            <div className="mx-auto flex min-h-full w-full max-w-4xl flex-col px-4 py-4 sm:px-8">
              {messages.length === 0 && !busy && !uploading ? (
                <div className="flex flex-1 flex-col items-center justify-center gap-3 text-center">
                  <div className="flex size-11 items-center justify-center rounded-2xl bg-accent text-accent-ink">
                    <span className="font-[family-name:var(--font-syne)] text-lg font-bold">
                      M
                    </span>
                  </div>
                  <h2 className="font-[family-name:var(--font-serif)] text-3xl font-medium tracking-tight text-ink">
                    How can I help?
                  </h2>
                </div>
              ) : (
                <div className="space-y-8 pb-4">
                  {messages.map((message) => (
                    <div
                      key={message.id}
                      className={`animate-message-in group/msg ${
                        message.role === "user"
                          ? "ml-auto max-w-[min(85%,36rem)]"
                          : message.role === "system"
                            ? "mx-auto max-w-md text-center"
                            : "w-full"
                      }`}
                    >
                      {message.role === "system" ? (
                        <MessageContent
                          content={message.content}
                          className="text-sm text-muted !font-[family-name:var(--font-sans)]"
                        />
                      ) : editingId === message.id ? (
                        <div className="space-y-2 rounded-2xl bg-elevated p-3 shadow-[var(--composer-shadow)]">
                          <textarea
                            value={editDraft}
                            onChange={(e) => setEditDraft(e.target.value)}
                            rows={3}
                            className="w-full resize-none bg-transparent text-[0.95rem] text-ink outline-none"
                          />
                          <div className="flex justify-end gap-2">
                            <button
                              type="button"
                              className="rounded-full px-3 py-1 text-xs text-muted hover:bg-hover hover:text-ink"
                              onClick={() => setEditingId(null)}
                            >
                              Cancel
                            </button>
                            <button
                              type="button"
                              className="rounded-full bg-accent px-3 py-1 text-xs font-semibold text-accent-ink"
                              onClick={() => void saveEdit(message.id)}
                            >
                              Save &amp; resend
                            </button>
                          </div>
                        </div>
                      ) : message.role === "user" ? (
                        <>
                          <div className="flex flex-col items-end gap-2">
                            {message.attachment ? (
                              <AttachmentPreview
                                file={message.attachment}
                                size="message"
                              />
                            ) : null}
                            {message.content.trim() ? (
                              <div className="rounded-3xl bg-user px-5 py-3 text-white">
                                <p className="whitespace-pre-wrap text-[0.95rem] leading-relaxed">
                                  {message.content}
                                </p>
                              </div>
                            ) : null}
                          </div>
                          <div className="mt-1.5 flex items-center justify-end gap-1 opacity-0 transition-opacity group-hover/msg:opacity-100">
                            <button
                              type="button"
                              aria-label="Copy"
                              title="Copy"
                              className="rounded-md p-1 text-muted hover:text-ink"
                              onClick={() => void copyMessage(message)}
                            >
                              {copiedId === message.id ? (
                                <Check className="size-3.5" />
                              ) : (
                                <Copy className="size-3.5" />
                              )}
                            </button>
                            <button
                              type="button"
                              aria-label="Edit"
                              title="Edit"
                              className="rounded-md p-1 text-muted hover:text-ink"
                              disabled={busy}
                              onClick={() => startEdit(message)}
                            >
                              <Pencil className="size-3.5" />
                            </button>
                            {message.sentAt ? (
                              <span className="px-1 text-[0.65rem] text-muted">
                                {formatTime(message.sentAt)}
                              </span>
                            ) : null}
                          </div>
                        </>
                      ) : (
                        <>
                          <MessageContent content={message.content} />
                          <div className="mt-2 flex items-center gap-1 opacity-0 transition-opacity group-hover/msg:opacity-100">
                            <button
                              type="button"
                              aria-label="Copy"
                              title="Copy"
                              className="rounded-md p-1 text-muted hover:text-ink"
                              onClick={() => void copyMessage(message)}
                            >
                              {copiedId === message.id ? (
                                <Check className="size-3.5" />
                              ) : (
                                <Copy className="size-3.5" />
                              )}
                            </button>
                            <button
                              type="button"
                              aria-label={
                                speakingMessageId === message.id
                                  ? "Stop speaking"
                                  : "Speak reply"
                              }
                              title={
                                speakingMessageId === message.id
                                  ? "Stop speaking"
                                  : "Speak reply"
                              }
                              className={`rounded-md p-1 hover:text-ink ${
                                speakingMessageId === message.id
                                  ? "text-accent"
                                  : "text-muted"
                              }`}
                              onClick={() => toggleSpeakMessage(message)}
                            >
                              {speakingMessageId === message.id ? (
                                <VolumeX className="size-3.5" />
                              ) : (
                                <Volume2 className="size-3.5" />
                              )}
                            </button>
                          </div>
                        </>
                      )}
                    </div>
                  ))}

                  {busy || uploading ? (
                    <div className="flex items-center gap-1.5 py-2">
                      <span className="think-dot size-1.5 rounded-full bg-accent" />
                      <span className="think-dot size-1.5 rounded-full bg-accent" />
                      <span className="think-dot size-1.5 rounded-full bg-accent" />
                      <span className="sr-only">
                        {uploading ? "Uploading…" : "Thinking…"}
                      </span>
                    </div>
                  ) : null}
                  <div ref={bottomRef} />
                </div>
              )}
            </div>
          </div>

          <div className="w-full shrink-0 px-4 pb-5 pt-2 sm:px-8 sm:pb-6">
            <div className="mx-auto w-full max-w-5xl">
              {pendingConfirmation ? (
                <div className="mb-3">
                  <ConfirmActions
                    pending={pendingConfirmation}
                    busy={busy || uploading}
                    onConfirm={() => void resolvePending("confirm")}
                    onCancel={() => void resolvePending("cancel")}
                  />
                </div>
              ) : null}
              {voiceMode ? (
                <VoiceWave
                  active
                  level={micLevel}
                  speaking={ttsSpeaking}
                  capturing={capturing}
                  canConfirm={
                    !busy && !uploading && input.trim().length >= MIN_VOICE_CHARS
                  }
                  onCancel={exitVoiceMode}
                  onConfirm={confirmVoiceSend}
                />
              ) : (
                <form
                  className="flex flex-col gap-2 rounded-[28px] bg-elevated p-2 shadow-[var(--composer-shadow)]"
                  onSubmit={onSubmit}
                  onPaste={onComposerPaste}
                >
                  {stagedFile && stagedKind ? (
                    <div className="flex items-start px-2 pt-1">
                      <AttachmentPreview
                        file={{
                          folder: "uploads",
                          name: stagedFile.name,
                          kind: stagedKind,
                          downloadUrl: "#",
                        }}
                        localPreviewUrl={stagedPreviewUrl}
                        size="composer"
                        uploading={uploading}
                        onRemove={() => {
                          setStagedFile(null);
                          if (fileInputRef.current) fileInputRef.current.value = "";
                        }}
                      />
                    </div>
                  ) : null}
                  <div className="flex items-end gap-1">
                    <input
                      ref={fileInputRef}
                      type="file"
                      accept=".pdf,.docx,.xlsx,.pptx,.png,.jpg,.jpeg,.webp,.gif,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.openxmlformats-officedocument.presentationml.presentation,image/png,image/jpeg,image/webp,image/gif"
                      className="hidden"
                      onChange={(e) => {
                        const file = e.target.files?.[0];
                        if (file) stageFile(file);
                      }}
                    />
                    <button
                      type="button"
                      disabled={busy || uploading}
                      aria-label="Upload PDF, Office, or image"
                      title="Upload PDF, Office, or image (or paste)"
                      onClick={() => fileInputRef.current?.click()}
                      className="mb-0.5 flex size-10 shrink-0 items-center justify-center rounded-full text-muted transition-colors hover:bg-hover hover:text-ink disabled:opacity-40"
                    >
                      <Plus className="size-5" strokeWidth={2} />
                    </button>
                    <textarea
                      value={input}
                      onChange={(e) => {
                        setInput(e.target.value);
                        draftRef.current = e.target.value;
                      }}
                      onPaste={onComposerPaste}
                      placeholder="Write a message… (paste files or screenshots)"
                      rows={1}
                      disabled={busy || uploading}
                      className="max-h-40 min-h-[44px] flex-1 resize-none bg-transparent px-2 py-3 text-[0.95rem] text-ink outline-none placeholder:text-muted/70 disabled:opacity-60"
                      onKeyDown={(e) => {
                        if (e.key === "Enter" && !e.shiftKey) {
                          e.preventDefault();
                          void onSubmit(e);
                        }
                      }}
                    />
                    <button
                      type="button"
                      disabled={uploading || !speechSupported}
                      aria-label="Enter voice mode"
                      title={
                        speechSupported
                          ? "Voice mode (or say Hello / Hi)"
                          : "Speech not supported in this browser"
                      }
                      onClick={toggleVoiceMode}
                      className="mb-0.5 flex size-10 shrink-0 items-center justify-center rounded-full text-muted transition-colors hover:bg-hover hover:text-ink disabled:opacity-40"
                    >
                      <Mic className="size-5" strokeWidth={2} />
                    </button>
                    {busy ? (
                      <button
                        type="button"
                        aria-label="Stop"
                        onClick={stopGeneration}
                        className="mb-0.5 flex size-10 shrink-0 items-center justify-center rounded-full bg-danger text-white transition-transform duration-150 hover:scale-[1.04] active:scale-95"
                      >
                        <Square className="size-3.5" fill="currentColor" strokeWidth={0} />
                      </button>
                    ) : (
                      <button
                        type="submit"
                        disabled={!canSend}
                        aria-label="Send"
                        className="mb-0.5 flex size-10 shrink-0 items-center justify-center rounded-full bg-accent text-accent-ink transition-transform duration-150 hover:scale-[1.04] active:scale-95 disabled:cursor-not-allowed disabled:opacity-35 disabled:hover:scale-100"
                      >
                        <ArrowUp className="size-5" strokeWidth={2.25} />
                      </button>
                    )}
                  </div>
                </form>
              )}
              {error ? (
                <p className="mt-2 px-1 text-center text-sm text-danger">{error}</p>
              ) : null}
              <p className="mt-3 text-center text-[0.7rem] text-muted/80">
                Midas Assistant can make mistakes. Double-check important info.
              </p>
            </div>
          </div>
        </section>
      </main>

      {docsOpen && chatFiles.length > 0 ? (
        <aside className="flex w-[300px] shrink-0 flex-col border-l border-border/50 bg-sidebar">
          <div className="flex items-center justify-between px-4 pb-2 pt-4">
            <h2 className="text-sm font-medium text-ink">Content</h2>
            <button
              type="button"
              aria-label="Close files panel"
              onClick={() => setDocsOpen(false)}
              className="flex size-8 items-center justify-center rounded-lg text-muted hover:bg-hover hover:text-ink"
            >
              <X className="size-4" />
            </button>
          </div>
          <div className="flex-1 space-y-2 overflow-y-auto px-3 pb-4">
            {chatFiles.map((f) => (
              <a
                key={fileKey(f)}
                href={docHref(f.downloadUrl)}
                target="_blank"
                rel="noreferrer"
                className="block rounded-xl bg-elevated p-3 transition-colors hover:bg-hover"
              >
                <div className="truncate text-sm font-medium text-ink">{f.name}</div>
                <div className="mt-2">
                  <span className="rounded bg-hover px-1.5 py-0.5 text-[0.65rem] font-medium uppercase tracking-wide text-muted">
                    {f.kind}
                  </span>
                </div>
              </a>
            ))}
          </div>
        </aside>
      ) : null}
      </div>
    </div>
  );
}
