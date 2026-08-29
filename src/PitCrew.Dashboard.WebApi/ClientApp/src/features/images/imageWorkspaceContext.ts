import { useOutletContext } from 'react-router-dom';

import type { ImageBuildRequest, ImageCandidate, ImageRecipeRegistration } from './imagesApi';

export interface ImageWorkspaceData {
  readonly registrations: ReadonlyArray<ImageRecipeRegistration>;
  readonly registrationsTruncated: boolean;
  readonly requests: ReadonlyArray<ImageBuildRequest>;
  readonly requestsTruncated: boolean;
  readonly candidates: ReadonlyArray<ImageCandidate>;
  readonly candidatesTruncated: boolean;
}

export interface ImageWorkspaceContext {
  readonly tenantId: string;
  readonly antiforgeryToken: string;
  readonly canAdminister: boolean;
  readonly data: ImageWorkspaceData | null;
  readonly error: string | null;
  readonly isLoading: boolean;
  readonly refresh: () => void;
}

/** Reads the shared runner-image workspace projection from the parent route. */
export function useImageWorkspace(): ImageWorkspaceContext {
  return useOutletContext<ImageWorkspaceContext>();
}
