import { useParams } from 'react-router-dom';

import { RunnersPage } from './RunnersPage';

/** Hosts the cross-fleet runner view at its tenant-scoped route. */
export default function RunnersRoute() {
  const { tenantId = '' } = useParams();
  return <RunnersPage tenantId={tenantId} />;
}
