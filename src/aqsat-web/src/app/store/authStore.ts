import { create } from "zustand";
import { api, ApiError, getActiveOrgId, getToken, setActiveOrgId, setToken } from "../../lib/api";
import { clearPersistedWorkspace } from "../../lib/sessionStorage";
import { clearSessionIdentity, isSessionExpiredResume, lastLoginMobile, rememberSession } from "../../lib/sessionExpiry";

export interface OrganizationMembership {
  organizationId: string;
  organizationName: string;
  roleName: string;
}

export interface AuthUser {
  userId: string;
  displayName: string;
  activeOrganizationId: string;
  permissions: string[];
  organizations: OrganizationMembership[];
}

interface LoginResponse {
  token: string;
  expiresInMinutes: number;
}

interface AuthState {
  user: AuthUser | null;
  /** "idle" before the initial /auth/me attempt on app load; "loading" while a request is in
   * flight; "ready" once we know either a user or that no session exists. Rendering must wait for
   * "ready" so a stored-but-expired token doesn't flash the app before bouncing to login. */
  status: "idle" | "loading" | "ready";
  error: string | null;
  login: (mobile: string, password: string) => Promise<void>;
  logout: () => void;
  loadMe: () => Promise<void>;
  switchOrganization: (organizationId: string) => Promise<void>;
}

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  status: "idle",
  error: null,

  login: async (mobile, password) => {
    set({ error: null });
    const response = await api.post<LoginResponse>("/auth/login", { mobile, password });
    // B12 — an expired session deliberately left the workspace in place for its owner to resume;
    // a DIFFERENT mobile logging in instead must never inherit those tabs and drafts.
    if (isSessionExpiredResume() && lastLoginMobile() !== mobile) {
      clearPersistedWorkspace();
    }
    rememberSession(mobile, response.expiresInMinutes);
    setToken(response.token);
    await get().loadMe();
  },

  logout: () => {
    setToken(null);
    setActiveOrgId(null);
    clearPersistedWorkspace();
    clearSessionIdentity();
    set({ user: null, status: "ready", error: null });
  },

  loadMe: async () => {
    if (!getToken()) {
      set({ user: null, status: "ready" });
      return;
    }

    set({ status: "loading" });
    try {
      const me = await api.get<AuthUser>("/auth/me");
      // Persist whichever org actually resolved (server defaults to the caller's first
      // membership when X-Organization-Id was absent) so the next request stays consistent.
      setActiveOrgId(getActiveOrgId() ?? me.activeOrganizationId);
      set({ user: me, status: "ready", error: null });
    } catch (err) {
      setToken(null);
      setActiveOrgId(null);
      set({
        user: null,
        status: "ready",
        error: err instanceof ApiError ? err.message : "خطا در دریافت اطلاعات کاربر",
      });
    }
  },

  switchOrganization: async (organizationId) => {
    setActiveOrgId(organizationId);
    await get().loadMe();
  },
}));
