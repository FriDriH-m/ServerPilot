import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type PropsWithChildren,
} from "react";
import {
  serverPilotApi,
  type AuthenticationApi,
  type AuthenticationRequest,
  type AuthenticationSession,
} from "../api/server-pilot-api";

interface AuthContextValue {
  session: AuthenticationSession | null;
  isAuthenticated: boolean;
  login(request: AuthenticationRequest): Promise<void>;
  register(request: AuthenticationRequest): Promise<void>;
  logout(): void;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

interface AuthProviderProps extends PropsWithChildren {
  api?: AuthenticationApi;
}

export function AuthProvider({
  api = serverPilotApi,
  children,
}: AuthProviderProps) {
  const [session, setSession] = useState<AuthenticationSession | null>(null);

  useEffect(() => {
    if (!session) {
      return undefined;
    }

    const expiresAt = Date.parse(session.expiresAt);
    const remainingMilliseconds = expiresAt - Date.now();
    if (!Number.isFinite(expiresAt) || remainingMilliseconds <= 0) {
      setSession(null);
      return undefined;
    }

    const timeout = window.setTimeout(
      () => setSession(null),
      remainingMilliseconds,
    );
    return () => window.clearTimeout(timeout);
  }, [session]);

  const value = useMemo<AuthContextValue>(
    () => ({
      session,
      isAuthenticated: session !== null,
      async login(request) {
        setSession(await api.login(request));
      },
      async register(request) {
        setSession(await api.register(request));
      },
      logout() {
        setSession(null);
      },
    }),
    [api, session],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider.");
  }

  return context;
}
