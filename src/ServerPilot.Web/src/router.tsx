import {
  useEffect,
  useSyncExternalStore,
  type AnchorHTMLAttributes,
  type MouseEvent,
  type PropsWithChildren,
} from "react";

function notifyNavigation() {
  window.dispatchEvent(new PopStateEvent("popstate"));
}

export function navigate(path: string, replace = false) {
  if (replace) {
    window.history.replaceState(null, "", path);
  } else {
    window.history.pushState(null, "", path);
  }

  notifyNavigation();
}

export function usePathname(): string {
  return useSyncExternalStore(
    (handleNavigation) => {
      window.addEventListener("popstate", handleNavigation);
      return () => window.removeEventListener("popstate", handleNavigation);
    },
    () => window.location.pathname,
    () => window.location.pathname,
  );
}

interface NavigateProps {
  to: string;
  replace?: boolean;
}

export function Navigate({ to, replace = false }: NavigateProps) {
  useEffect(() => navigate(to, replace), [replace, to]);
  return null;
}

interface LinkProps
  extends PropsWithChildren,
    Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "href"> {
  to: string;
}

export function Link({ to, onClick, children, ...props }: LinkProps) {
  function handleClick(event: MouseEvent<HTMLAnchorElement>) {
    onClick?.(event);
    if (
      event.defaultPrevented ||
      event.button !== 0 ||
      event.metaKey ||
      event.ctrlKey ||
      event.shiftKey ||
      event.altKey
    ) {
      return;
    }

    event.preventDefault();
    navigate(to);
  }

  return (
    <a href={to} onClick={handleClick} {...props}>
      {children}
    </a>
  );
}
