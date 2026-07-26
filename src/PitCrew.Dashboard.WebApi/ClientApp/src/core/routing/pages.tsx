import { isRouteErrorResponse, Link, Navigate, useLocation, useRouteError } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import { PitCrewBrand } from '@/core/branding/PitCrewBrand';

/** Selects the deterministic authenticated landing route. */
export function RootRedirect() {
  const { session } = useSession();
  const firstTenant = session?.tenants[0];
  if (firstTenant) return <Navigate to={`/tenants/${firstTenant.tenantId}/fleet`} replace />;
  if (session?.isSystemAdministrator) return <Navigate to="/admin/tenants" replace />;
  return <Navigate to="/no-access" replace />;
}

/** Explains that an authenticated identity has no authorized tenant. */
export function NoAccessPage() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>No tenant access</CardTitle>
        <CardDescription>
          Ask a tenant owner to add your GitHub user after this first sign-in.
        </CardDescription>
      </CardHeader>
    </Card>
  );
}

/** Explains that no registered dashboard route matches the requested URL. */
export function NotFoundPage() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Page not found</CardTitle>
        <CardDescription>
          This dashboard route does not exist or is no longer available.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Button asChild>
          <Link to="/">Return to the dashboard</Link>
        </Button>
      </CardContent>
    </Card>
  );
}

/** Contains an unexpected route failure without exposing internal exception details. */
export function RouteErrorPage() {
  const error = useRouteError();
  const status = isRouteErrorResponse(error) ? ` (${error.status})` : '';
  return (
    <main className="mx-auto flex min-h-screen max-w-xl items-center px-4" role="alert">
      <Card className="w-full">
        <CardHeader>
          <CardTitle>Page could not be displayed{status}</CardTitle>
          <CardDescription>
            An unexpected routing error occurred. Return to the dashboard and try again.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button asChild>
            <a href="/">Return to the dashboard</a>
          </Button>
        </CardContent>
      </Card>
    </main>
  );
}

/** Renders an authentication link that preserves the complete local deep link. */
export function LoginPage() {
  const location = useLocation();
  const returnUrl = `${location.pathname}${location.search}`;
  const loginUrl = `/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;

  return (
    <main className="mx-auto flex min-h-screen max-w-xl items-center px-4">
      <Card className="w-full">
        <CardHeader className="items-center text-center">
          <PitCrewBrand variant="hero" />
          <CardTitle className="mt-2 text-2xl">Sign in to PitCrew Dashboard</CardTitle>
          <CardDescription>
            Fleet data and connector administration require an authorized GitHub account.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button asChild>
            <a href={loginUrl}>Sign in with GitHub</a>
          </Button>
        </CardContent>
      </Card>
    </main>
  );
}
