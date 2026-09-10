import { emitDataChanged } from "./dataEvents";

const TOKEN_KEY = "aqsat_token";
const ORG_KEY = "aqsat_active_org";

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string | null): void {
  if (token) localStorage.setItem(TOKEN_KEY, token);
  else localStorage.removeItem(TOKEN_KEY);
}

export function getActiveOrgId(): string | null {
  return localStorage.getItem(ORG_KEY);
}

export function setActiveOrgId(id: string | null): void {
  if (id) localStorage.setItem(ORG_KEY, id);
  else localStorage.removeItem(ORG_KEY);
}

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

/** Fires when a 401 clears the session — App.tsx listens to fall back to the login screen
 * without a hard page reload (which would lose in-memory tab state elsewhere). */
export const AUTH_CLEARED_EVENT = "aqsat:auth-cleared";

/** B12 — marks "the workspace currently on disk was left by an expired session"; read by
 * authStore.login (via lib/sessionExpiry) to decide whether its owner is the one resuming. */
export const SESSION_EXPIRED_KEY = "aqsat_session_expired";

/** B11 — a hung backend (IIS accepted the connection but never answers) would otherwise spin
 * forever. Uploads (FormData) get a much longer leash: they carry real bytes. */
const REQUEST_TIMEOUT_MS = 30_000;
const UPLOAD_TIMEOUT_MS = 300_000;

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers);
  const token = getToken();
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }
  const orgId = getActiveOrgId();
  if (orgId) {
    headers.set("X-Organization-Id", orgId);
  }
  if (options.body && !(options.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const controller = new AbortController();
  const timeout = setTimeout(
    () => controller.abort(),
    options.body instanceof FormData ? UPLOAD_TIMEOUT_MS : REQUEST_TIMEOUT_MS,
  );

  let response: Response;
  try {
    response = await fetch(`/api${path}`, { ...options, headers, signal: controller.signal });
  } catch (err) {
    if (err instanceof DOMException && err.name === "AbortError") {
      throw new ApiError(0, "پاسخی از سرور در مهلت مقرر دریافت نشد. لطفاً دوباره تلاش کنید.");
    }
    // fetch() itself throws only for a connectivity failure (server unreachable, DNS, CORS) —
    // never for a non-2xx HTTP response, which is handled below instead. Distinguishing this from
    // a generic server error is the whole point: "the backend isn't running" is diagnosable, an
    // unlabeled "خطای غیرمنتظره" is not.
    throw new ApiError(0, "امکان برقراری ارتباط با سرور نیست. از اجرا بودن سرویس backend مطمئن شوید.");
  } finally {
    clearTimeout(timeout);
  }

  // The login endpoint's own 401 means "wrong mobile/password", not "your session expired" — only
  // treat a 401 on an already-authenticated request as session expiry.
  if (response.status === 401 && path !== "/auth/login") {
    setToken(null);
    setActiveOrgId(null);
    // B12 — the tabs/drafts are NOT wiped here anymore. The session keys above are what gate
    // data access; the workspace stays so the same user can log back in and resume (the banner
    // in lib/sessionExpiry warns 10 minutes before this happens). authStore.login wipes it if a
    // DIFFERENT mobile shows up.
    sessionStorage.setItem(SESSION_EXPIRED_KEY, "1");
    window.dispatchEvent(new Event(AUTH_CLEARED_EVENT));
    throw new ApiError(401, "نشست شما منقضی شده است.");
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { title?: string } | null;
    // 502/503/504 with no parseable JSON body means the request never reached the API at all —
    // Vite's dev proxy (or a production reverse proxy) generated the error page itself because the
    // backend process is down/unreachable. That is a distinct, diagnosable situation from an actual
    // application error and deserves its own message, not the generic fallback.
    if (problem === null && [502, 503, 504].includes(response.status)) {
      throw new ApiError(response.status, "سرور در دسترس نیست. از اجرا بودن سرویس backend مطمئن شوید.");
    }
    // [Authorize(Policy = ...)] failures are handled by ASP.NET Core's own authorization
    // middleware, not our controllers — its default 403 response has no JSON body at all, so
    // without this the generic fallback would fire on every ordinary "you don't have this
    // permission" case, which is common and expected (e.g. a Platform.Owner-only account browsing
    // an agency page), not actually unexpected.
    if (problem === null && response.status === 403) {
      throw new ApiError(403, "شما دسترسی لازم برای این بخش را ندارید.");
    }
    throw new ApiError(response.status, problem?.title ?? "خطای غیرمنتظره رخ داد.");
  }

  if (response.status === 204) {
    notifyMutation(options.method);
    return undefined as T;
  }

  notifyMutation(options.method);
  return (await response.json()) as T;
}

/** A successful non-GET means server data changed — tell the live-reload subscribers. */
function notifyMutation(method: string | undefined): void {
  if (method && method !== "GET") emitDataChanged();
}

export const api = {
  get: <T,>(path: string) => request<T>(path),
  post: <T,>(path: string, body?: unknown) =>
    request<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T,>(path: string, body?: unknown) =>
    request<T>(path, { method: "PUT", body: body === undefined ? undefined : JSON.stringify(body) }),
  delete: <T,>(path: string, body?: unknown) =>
    request<T>(path, { method: "DELETE", body: body === undefined ? undefined : JSON.stringify(body) }),
  postForm: <T,>(path: string, form: FormData) => request<T>(path, { method: "POST", body: form }),
};
