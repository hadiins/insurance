import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import { authApi, getStoredToken, setStoredToken, type AuthUser } from "../api/client";

const USER_STORAGE_KEY = "auth_user";

function getStoredUser(): AuthUser | null {
  const raw = localStorage.getItem(USER_STORAGE_KEY);
  return raw ? (JSON.parse(raw) as AuthUser) : null;
}

function setStoredUser(user: AuthUser | null): void {
  if (user) localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
  else localStorage.removeItem(USER_STORAGE_KEY);
}

interface AuthContextValue {
  user: AuthUser | null;
  login: (email: string, password: string) => Promise<void>;
  register: (fullName: string, email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(() =>
    getStoredToken() ? getStoredUser() : null
  );

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      login: async (email, password) => {
        const { token, user: loggedInUser } = await authApi.login(email, password);
        setStoredToken(token);
        setStoredUser(loggedInUser);
        setUser(loggedInUser);
      },
      register: async (fullName, email, password) => {
        const { token, user: registeredUser } = await authApi.register(fullName, email, password);
        setStoredToken(token);
        setStoredUser(registeredUser);
        setUser(registeredUser);
      },
      logout: () => {
        setStoredToken(null);
        setStoredUser(null);
        setUser(null);
      },
    }),
    [user]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth باید داخل AuthProvider استفاده شود");
  return ctx;
}
