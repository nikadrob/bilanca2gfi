const amountFormat = new Intl.NumberFormat('hr-HR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export const formatAmount = (value: number) => amountFormat.format(value);

/** "2026-12-31" → "31.12.2026." */
export function formatDate(iso: string | null): string {
  if (!iso) return '—';
  const [y, m, d] = iso.split('-');
  return `${d}.${m}.${y}.`;
}
