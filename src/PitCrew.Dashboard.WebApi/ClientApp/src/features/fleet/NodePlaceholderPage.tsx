import { useParams } from 'react-router-dom';

import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';

/** Temporary node route until the dedicated node-detail issue lands. */
export default function NodePlaceholderPage() {
  const { nodeId } = useParams();
  return (
    <Card>
      <CardHeader>
        <CardTitle>Node {nodeId}</CardTitle>
        <CardDescription>
          Node detail will be added by the fleet decomposition work.
        </CardDescription>
      </CardHeader>
    </Card>
  );
}
