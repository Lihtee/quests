// Pure task rules (no React, Dexie or fetch). Limits match the server's TaskItem.

export const TITLE_MAX_LENGTH = 200
export const NOTES_MAX_LENGTH = 4000

export interface SortableTask {
  dueAt: string | null
  completedAt: string | null
  createdAt: string
}

export interface TaskText {
  title: string
  notes: string | null
}

export type TaskErrors = Partial<Record<keyof TaskText, string>>

const time = (iso: string) => Date.parse(iso)

function compareOpen(a: SortableTask, b: SortableTask): number {
  if (a.dueAt !== b.dueAt) {
    if (a.dueAt === null) return 1
    if (b.dueAt === null) return -1
    const byDue = time(a.dueAt) - time(b.dueAt)
    if (byDue !== 0) return byDue
  }
  return time(a.createdAt) - time(b.createdAt)
}

/** Open tasks first (due ascending, undated last, then created); completed after, newest completion first. */
export function sortTasks<T extends SortableTask>(tasks: readonly T[]): T[] {
  const open = tasks.filter((t) => t.completedAt === null).sort(compareOpen)
  const completed = tasks
    .filter((t) => t.completedAt !== null)
    .sort((a, b) => time(b.completedAt!) - time(a.completedAt!))
  return [...open, ...completed]
}

/** Field errors for user input; an empty object means valid. */
export function validateTask({ title, notes }: TaskText): TaskErrors {
  const errors: TaskErrors = {}
  const trimmed = title.trim()
  if (trimmed === '') errors.title = 'Title is required.'
  else if (trimmed.length > TITLE_MAX_LENGTH) errors.title = `Title must be at most ${TITLE_MAX_LENGTH} characters.`

  if (notes !== null && notes.length > NOTES_MAX_LENGTH) errors.notes = `Notes must be at most ${NOTES_MAX_LENGTH} characters.`
  return errors
}

/** Trims the title; blank notes become null (same as the server). */
export function normalizeTask({ title, notes }: TaskText): TaskText {
  return { title: title.trim(), notes: notes === null || notes.trim() === '' ? null : notes }
}
