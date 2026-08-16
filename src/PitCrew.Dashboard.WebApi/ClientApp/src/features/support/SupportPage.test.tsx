import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { SupportSessionCard } from './SupportPage';

describe('SupportSessionCard', () => {
  it('renders verified support output without interpreting diagnostic markdown as HTML', () => {
    render(
      <SupportSessionCard
        session={{
          sessionId: '22222222-2222-2222-2222-222222222222',
          nodeId: '11111111-1111-1111-1111-111111111111',
          diagnosticMode: 'ConnectorOffline',
          profileId: null,
          capability: 'pitcrew.diagnostics.snapshot.v1',
          requestDigest: 'b'.repeat(64),
          nodeSigningKeyFingerprint: 'a'.repeat(64),
          status: 'Completed',
          requestedAt: '2026-08-01T00:00:00+00:00',
          expiresAt: '2026-08-01T00:05:00+00:00',
          result: {
            report: { verified: ['connector'], unavailable: [], hypotheses: [] },
            markdown: '<script>alert(1)</script> verified evidence',
            attestation: {
              nodeSigningPublicKeySpki: 'spki',
              payloadBase64Url: 'payload',
              signatureBase64Url: 'signature',
              signatureAlgorithm: 'ES256-P1363',
            },
          },
        }}
      />,
    );

    expect(screen.getByText('ConnectorOffline')).toBeInTheDocument();
    expect(screen.getByText('<script>alert(1)</script> verified evidence')).toBeInTheDocument();
    expect(screen.getByText(/Attestation ES256-P1363/i)).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
  });
});
