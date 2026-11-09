import { normalizeTask, validateTask, type TaskErrors } from '../../domain/taskRules'
import type { OutboxEntry, QuestsDb, Task, TaskField } from '../../shared/db'
import { requestSync } from '../sync/syncEngine'

/** User-editable task fields. Dates are ISO 8601 strings. */
export interface TaskInput {
  title: string
  notes: string | null
  dueAt: string | null
}

export class TaskValidationError extends Error {
  readonly errors: TaskErrors

  constructor(errors: TaskErrors) {
    super('Task is invalid')
    this.name = 'TaskValidationError'
    this.errors = errors
  }
}

const allFields: TaskField[] = ['title', 'notes', 'dueAt', 'completedAt']

/** Creates a task locally (client-generated id) and queues it for sync. Returns the new id. */
export async function createTask(db: QuestsDb, input: TaskInput, now = new Date()): Promise<string> {
  const at = now.toISOString()
  const task: Task = {
    id: crypto.randomUUID(),
    ...validInput(input),
    completedAt: null,
    createdAt: at,
    updatedAt: at,
    version: 0,
    deletedAt: null,
  }

  await db.transaction('rw', db.tasks, db.outbox, async () => {
    await db.tasks.add(task)
    await db.outbox.add(entry(task, 'upsert', allFields, at))
  })
  requestSync(db)
  return task.id
}

/** Saves edited fields; only fields that differ from the local row are queued. */
export async function updateTask(db: QuestsDb, id: string, input: TaskInput, now = new Date()): Promise<void> {
  const values = validInput(input)
  await change(db, id, 'upsert', now, (task) => {
    // Only real changes, so an unchanged field never overwrites another device's edit of it.
    const changed: TaskField[] = []
    if (task.title !== values.title) changed.push('title')
    if (task.notes !== values.notes) changed.push('notes')
    if (!sameInstant(task.dueAt, values.dueAt)) changed.push('dueAt')
    return { patch: values, changed }
  })
}

export const completeTask = (db: QuestsDb, id: string, now = new Date()) =>
  change(db, id, 'upsert', now, () => ({ patch: { completedAt: now.toISOString() }, changed: ['completedAt'] }))

export const reopenTask = (db: QuestsDb, id: string, now = new Date()) =>
  change(db, id, 'upsert', now, () => ({ patch: { completedAt: null }, changed: ['completedAt'] }))

/** Marks the task deleted locally (hidden at once); the sync engine removes the row once the server agrees. */
export const deleteTask = (db: QuestsDb, id: string, now = new Date()) =>
  change(db, id, 'delete', now, () => ({ patch: { deletedAt: now.toISOString() }, changed: [] }))

function validInput(input: TaskInput): TaskInput {
  const errors = validateTask(input)
  if (Object.keys(errors).length > 0) throw new TaskValidationError(errors)
  return { ...normalizeTask(input), dueAt: input.dueAt }
}

/** The server and the browser format the same instant differently ("+00:00" vs "Z"). */
function sameInstant(a: string | null, b: string | null): boolean {
  if (a === null || b === null) return a === b
  return Date.parse(a) === Date.parse(b)
}

function entry(task: Task, op: OutboxEntry['op'], changed: TaskField[], at: string): OutboxEntry {
  return { taskId: task.id, op, changed, baseVersion: task.version, at }
}

/** Writes the row and its outbox entry in one transaction, then schedules a sync. */
async function change(
  db: QuestsDb,
  id: string,
  op: OutboxEntry['op'],
  now: Date,
  build: (task: Task) => { patch: Partial<Task>; changed: TaskField[] },
): Promise<void> {
  const at = now.toISOString()
  const queued = await db.transaction('rw', db.tasks, db.outbox, async () => {
    const task = await db.tasks.get(id)
    if (!task || task.deletedAt !== null) throw new Error('Task not found')

    const { patch, changed } = build(task)
    if (op === 'upsert' && changed.length === 0) return false

    await db.tasks.put({ ...task, ...patch, updatedAt: at })
    await db.outbox.add(entry(task, op, changed, at))
    return true
  })
  if (queued) requestSync(db)
}
