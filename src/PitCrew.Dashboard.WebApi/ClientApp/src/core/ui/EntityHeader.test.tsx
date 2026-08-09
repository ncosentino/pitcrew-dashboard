import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { CopyableId } from '@/core/ui/CopyableId';
import { EntityHeader } from '@/core/ui/EntityHeader';

describe('EntityHeader', () => {
  it('renders the entity title as an h2 by default with its identifier as secondary text', () => {
    render(<EntityHeader title="Alpha" identifier="node-1" />);

    expect(screen.getByRole('heading', { level: 2, name: 'Alpha' })).toBeInTheDocument();
    expect(screen.getByText('node-1')).toBeInTheDocument();
  });

  it('renders at the requested heading level when nested under another entity heading', () => {
    render(<EntityHeader title="build" identifier="build" headingLevel="h3" />);

    expect(screen.getByRole('heading', { level: 3, name: 'build' })).toBeInTheDocument();
  });

  it('renders a custom identifier node without forcing it into plain text', () => {
    render(
      <EntityHeader
        title="Alpha"
        identifier={<CopyableId label="node identifier" value="node-1" />}
      />,
    );

    expect(screen.getByRole('button', { name: 'Copy node identifier' })).toBeInTheDocument();
  });

  it('omits the identifier paragraph when the entity has none', () => {
    render(<EntityHeader title="Alpha" />);

    expect(screen.getByRole('heading', { level: 2, name: 'Alpha' })).toBeInTheDocument();
    expect(screen.queryByText('node-1')).not.toBeInTheDocument();
  });

  it('renders actions alongside the title', () => {
    render(
      <EntityHeader
        title="Alpha"
        identifier="node-1"
        actions={<button type="button">Revoke node</button>}
      />,
    );

    expect(screen.getByRole('button', { name: 'Revoke node' })).toBeInTheDocument();
  });
});
