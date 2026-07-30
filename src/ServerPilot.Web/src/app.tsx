import { useAuth } from "./auth/auth-context";
import { AuthPage } from "./pages/auth-page";
import { WorkspacePage } from "./pages/workspace-page";
import { Navigate, usePathname } from "./router";

export function AppRoutes() {
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
    return <WorkspacePage />;
  }

  return <Navigate to="/app" replace />;
}
