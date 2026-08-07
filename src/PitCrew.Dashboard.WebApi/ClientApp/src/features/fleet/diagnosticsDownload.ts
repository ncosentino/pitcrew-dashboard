/** Downloads one schema-bound remote-diagnostics preflight document. */
export function downloadDiagnosticsContext(nodeId: string, content: string): void {
  const blob = new Blob([content], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `pitcrew-diagnostics-preflight-${nodeId}.json`;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
