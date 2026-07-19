import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Button } from '@/components/ui/button';

describe('Button', () => {
  it('renders its children inside a button element', () => {
    render(<Button>Click me</Button>);

    const button = screen.getByRole('button', { name: 'Click me' });
    expect(button).toBeInTheDocument();
    expect(button).toHaveAttribute('data-slot', 'button');
  });

  it('renders as the child element when asChild is set', () => {
    render(
      <Button asChild>
        <a href="/somewhere">Go somewhere</a>
      </Button>,
    );

    const link = screen.getByRole('link', { name: 'Go somewhere' });
    expect(link).toBeInTheDocument();
  });
});
