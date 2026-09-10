import { AUTH_CLEARED_EVENT, setActiveOrgId, setToken } from "./api";

/// B12 — the 8-hour token used to die on the first request after expiry, and api.ts's 401 path
/// wiped the whole persisted workspace on the spot: a half-filled 20-field form was destroyed
/// without a word. These helpers move that to a warned, resumable flow: the workspace survives
/// an expired session, the banner warns 10 minutes ahead, and only a DIFFERENT mobile logging
/// in afterwards loses it (authStore.login enforces that).
const LOGIN_MOBILE_KEY = "aqsat_login_mobile";
const EXPIRES_KEY = "aqsat_session_expires_at";

/** Set by api.ts's 401 path and beginRelogin; read by authStore.login to decide whether the
 * workspace left behind belongs to the user now logging in. sessionStorage so a browser restart
 * (a genuinely abandoned session) never resurrects it. */
export const SESSION_EXPIRED_KEY = "aqsat_session_expired";

export const SESSION_WARNING_MS = 10 * 60 * 1000;

export function rememberSession(mobile: string, expiresInMinutes: number): void {
  localStorage.setItem(LOGIN_MOBILE_KEY, mobile);
  localStorage.setItem(EXPIRES_KEY, String(Date.now() + expiresInMinutes * 60_000));
  sessionStorage.removeItem(SESSION_EXPIRED_KEY);
}

export function clearSessionIdentity(): void {
  localStorage.removeItem(LOGIN_MOBILE_KEY);
  localStorage.removeItem(EXPIRES_KEY);
  sessionStorage.removeItem(SESSION_EXPIRED_KEY);
}

export function isSessionExpiredResume(): boolean {
  return sessionStorage.getItem(SESSION_EXPIRED_KEY) === "1";
}

export function lastLoginMobile(): string | null {
  return localStorage.getItem(LOGIN_MOBILE_KEY);
}

export function msUntilExpiry(): number | null {
  const raw = localStorage.getItem(EXPIRES_KEY);
  if (!raw) return null;
  const at = Number(raw);
  return Number.isFinite(at) ? at - Date.now() : null;
}

/** Voluntary re-login from the pre-expiry banner — identical to the 401 path: session keys are
 * dropped but the workspace is kept for the same user's return. */
export function beginRelogin(): void {
  setToken(null);
  setActiveOrgId(null);
  sessionStorage.setItem(SESSION_EXPIRED_KEY, "1");
  window.dispatchEvent(new Event(AUTH_CLEARED_EVENT));
}
