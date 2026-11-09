const pad = (n: number) => String(n).padStart(2, '0')

/** ISO string → value for `<input type="datetime-local">` in local time ("" when null). */
export function toLocalInputValue(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** `datetime-local` value (local time) → ISO string in UTC; empty → null. */
export function fromLocalInputValue(value: string): string | null {
  if (!value) return null
  return new Date(value).toISOString()
}

const dueFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

/** Short local date/time for display; "—" when null. */
export function formatDue(iso: string | null): string {
  if (!iso) return '—'
  return dueFormat.format(new Date(iso))
}
