import { useMemo } from 'react';
import { RouterProvider } from 'react-router-dom';

import { SessionProvider } from '@/core/auth';
import { createAppRouter } from '@/core/routing/createAppRouter';
import { features } from '@/features.registry';

/** Bootstraps the session and registered feature route graph. */
function App() {
  const router = useMemo(() => createAppRouter(features), []);
  return (
    <SessionProvider>
      <RouterProvider router={router} />
    </SessionProvider>
  );
}

export default App;
