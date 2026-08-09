import { Outlet } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import { PitCrewBrand } from '@/core/branding/PitCrewBrand';
import { LoadingState } from '@/core/ui/LoadingState';

import { LoginPage } from './pages';

/** Converts session bootstrap state into the authenticated route boundary. */
export function SessionBoundary() {
  const { status, error, refreshSession } = useSession();
  if (status === 'loading') {
    return (
      <main className="mx-auto flex min-h-screen max-w-xl items-center px-4">
        <Card className="w-full">
          <CardHeader className="items-center text-center">
            <PitCrewBrand variant="hero" />
            <CardTitle as="h1" className="mt-2 text-2xl">
              Opening PitCrew Dashboard
            </CardTitle>
            <CardDescription>Loading authorized fleet access.</CardDescription>
          </CardHeader>
          <CardContent>
            <LoadingState label="Loading dashboard session…" />
          </CardContent>
        </Card>
      </main>
    );
  }
  if (status === 'unauthenticated') return <LoginPage />;
  if (status === 'error') {
    return (
      <main className="mx-auto flex min-h-screen max-w-xl items-center px-4">
        <Card className="w-full" role="alert">
          <CardHeader className="items-center text-center">
            <PitCrewBrand variant="hero" />
            <CardTitle as="h1" className="mt-2 text-2xl">
              Dashboard session is unavailable
            </CardTitle>
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
