import { useAuth } from "./auth/auth-context";
import type { ManagementApi } from "./api/server-pilot-api";
import { AuthPage } from "./pages/auth-page";
import { WorkspacePage } from "./pages/workspace-page";
import { Navigate, usePathname } from "./router";

interface AppRoutesProps {
  managementApi?: ManagementApi;
}

export function AppRoutes({ managementApi }: AppRoutesProps) {
  const pathname = usePathname();
  const { isAuthenticated } = useAuth();

  if (!isAuthenticated && pathname !== "/login" && pathname !== "/register") {
    const returnTo = pathname !== "/" ? `?returnTo=${encodeURIComponent(pathname)}` : "";
    return <Navigate to={`/login${returnTo}`} replace />;
  }

  if (isAuthenticated && (pathname === "/login" || pathname === "/register")) {
    return <Navigate to="/app" replace />;
  }

  if (pathname === "/login") {
    return <AuthPage mode="login" />;
  }

  if (pathname === "/register") {
    return <AuthPage mode="register" />;
  }

  if (pathname === "/app" && isAuthenticated) {
    return <WorkspacePage api={managementApi} />;
  }

  return <Navigate to="/app" replace />;
}
