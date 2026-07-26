import { useParams } from 'react-router-dom';

import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';

/** Temporary profile route until the dedicated profile-detail issue lands. */
export default function ProfilePlaceholderPage() {
  const { profileId } = useParams();
  return (
    <Card>
      <CardHeader>
        <CardTitle>Profile {profileId}</CardTitle>
        <CardDescription>
          Profile detail will be added by the fleet decomposition work.
        </CardDescription>
      </CardHeader>
    </Card>
  );
}
