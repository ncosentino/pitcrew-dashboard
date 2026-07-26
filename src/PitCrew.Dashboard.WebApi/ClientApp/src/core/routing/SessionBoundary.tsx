import { Outlet } from 'react-router-dom';

import { useSession } from '@/core/auth';

import { LoginPage } from './pages';

/** Converts session bootstrap state into the authenticated route boundary. */
export function SessionBoundary() {
  const { status, error } = useSession();
  if (status === 'loading') {
    return <main className="p-8 text-muted-foreground">Loading dashboard session…</main>;
  }
  if (status === 'unauthenticated') return <LoginPage />;
  if (status === 'error') {
    return <main className="p-8 text-red-700">{error ?? 'Dashboard session is unavailable.'}</main>;
  }
  return <Outlet />;
}
