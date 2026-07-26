import { Outlet } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';

import { LoginPage } from './pages';

/** Converts session bootstrap state into the authenticated route boundary. */
export function SessionBoundary() {
  const { status, error, refreshSession } = useSession();
  if (status === 'loading') {
    return <main className="p-8 text-muted-foreground">Loading dashboard session…</main>;
  }
  if (status === 'unauthenticated') return <LoginPage />;
  if (status === 'error') {
    return (
      <main className="mx-auto flex min-h-screen max-w-xl items-center px-4" role="alert">
        <Card className="w-full">
          <CardHeader>
            <CardTitle>Dashboard session is unavailable</CardTitle>
            <CardDescription>
              {error ?? 'The authenticated dashboard session could not be loaded.'}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button type="button" onClick={() => void refreshSession()}>
              Retry session
            </Button>
          </CardContent>
        </Card>
      </main>
    );
  }
  return <Outlet />;
}
