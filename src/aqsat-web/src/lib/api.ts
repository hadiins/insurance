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

  const response = await fetch(`/api${path}`, { ...options, headers });

  if (response.status === 401) {
    setToken(null);
    setActiveOrgId(null);
    window.dispatchEvent(new Event(AUTH_CLEARED_EVENT));
    throw new ApiError(401, "نشست شما منقضی شده است.");
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { title?: string } | null;
    throw new ApiError(response.status, problem?.title ?? "خطای غیرمنتظره رخ داد.");
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  get: <T,>(path: string) => request<T>(path),
  post: <T,>(path: string, body?: unknown) =>
    request<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T,>(path: string, body?: unknown) =>
    request<T>(path, { method: "PUT", body: body === undefined ? undefined : JSON.stringify(body) }),
  postForm: <T,>(path: string, form: FormData) => request<T>(path, { method: "POST", body: form }),
};
