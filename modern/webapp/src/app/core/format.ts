/** Presentation helpers kept free of Angular so they are unit-testable anywhere. */

/** Stable string for ISO timestamps in tables. */
export function shortDate(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString('en-GB', { dateStyle: 'medium', timeStyle: 'short' });
}

/** Only date portion for list views. */
export function shortDay(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString('en-GB', { dateStyle: 'medium' });
}

/** Money-ish display for amounts already carrying their culture context. */
export function amount(value: number | string): string {
  const n = typeof value === 'number' ? value : Number(value);
  return Number.isNaN(n) ? String(value) : n.toLocaleString('en-EG', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** Status badge CSS modifier — lowercase vocabulary maps straight to classes. */
export function statusClass(status: string): string {
  return status.toLowerCase();
}
