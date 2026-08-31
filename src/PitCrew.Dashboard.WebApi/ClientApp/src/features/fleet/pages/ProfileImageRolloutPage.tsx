import { ProfileImageRollout } from '../components/ProfileImageRollout';
import { useProfileDetail } from './ProfilePages';

/** Binds the profile route context to the independently lazy-loaded rollout workflow. */
export default function ProfileImageRolloutPage() {
  const { tenantId, node, profile, canAdminister, antiforgeryToken } = useProfileDetail();
  return (
    <ProfileImageRollout
      tenantId={tenantId}
      node={node}
      profile={profile}
      canAdminister={canAdminister}
      antiforgeryToken={antiforgeryToken}
    />
  );
}
