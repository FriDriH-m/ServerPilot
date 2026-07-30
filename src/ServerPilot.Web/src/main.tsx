import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { AppRoutes } from "./app";
import { AuthProvider } from "./auth/auth-context";
import "./styles.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("ServerPilot Web root element was not found.");
}

createRoot(root).render(
  <StrictMode>
    <AuthProvider>
      <AppRoutes />
    </AuthProvider>
  </StrictMode>,
);
