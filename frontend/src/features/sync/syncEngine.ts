import { ApiError, apiSend } from '../../shared/api'
import { readMeta, writeMeta, type OutboxEntry, type QuestsDb, type Task, type TaskField } from '../../shared/db'

/** Matches the API's TaskChange. Values are sent for every field; the server applies only `changed`. */
export interface TaskChange {
  id: string
  op: 'upsert' | 'delete'
  baseVersion: number
  changed: TaskField[]
  title: string | null
  notes: string | null
  dueAt: string | null
  completedAt: string | null
  createdAt: string | null
}

export interface SyncResponse {
  version: number
  tasks: Task[]
  rejected: { id: string; reason: string }[]
}

interface EngineState {
  running: Promise<void> | null
  rerun: boolean
  timer: ReturnType<typeof setTimeout> | null
}

const SYNC_DELAY_MS = 500
const CONNECTION_ID_HEADER = 'X-Sync-Connection-Id'
const states = new WeakMap<QuestsDb, EngineState>()
let senderConnectionId: string | null = null

/** The open sync-signal connection; sent with each sync so the server does not signal this device back. */
export function setSyncConnectionId(id: string | null): void {
  if (id === senderConnectionId) return
  senderConnectionId = id
  if (import.meta.env.DEV) console.debug('[sync] connection id', id)
}

function stateOf(db: QuestsDb): EngineState {
  let state = states.get(db)
  if (!state) {
    state = { running: null, rerun: false, timer: null }
    states.set(db, state)
  }
  return state
}

/** Schedules a sync shortly after a local write (debounced, so a burst of writes is pushed once). */
export function requestSync(db: QuestsDb): void {
  const state = stateOf(db)
  if (state.timer !== null) clearTimeout(state.timer)
  state.timer = setTimeout(() => {
    state.timer = null
    void syncNow(db)
  }, SYNC_DELAY_MS)
}

/** Cancels a scheduled (debounced) sync, e.g. on sign-out. */
export function cancelRequestedSync(db: QuestsDb): void {
  const state = stateOf(db)
  if (state.timer !== null) clearTimeout(state.timer)
  state.timer = null
}

/**
 * Pushes the outbox and pulls server changes. One sync at a time: a call during a running sync queues one rerun
 * and resolves when it is done. Never throws; on failure nothing changes locally and the next trigger retries.
 */
export function syncNow(db: QuestsDb): Promise<void> {
  const state = stateOf(db)
  if (state.running) {
    state.rerun = true
    return state.running
  }

  state.running = (async () => {
    try {
      let ok: boolean
      do {
        state.rerun = false
        ok = await syncOnce(db)
      } while (ok && state.rerun)
    } finally {
      state.running = null
    }
  })()
  return state.running
}

async function syncOnce(db: QuestsDb): Promise<boolean> {
  try {
    const entries = await db.outbox.orderBy('seq').toArray()
    const snapshotMax = entries.at(-1)?.seq ?? 0
    const since = (await readMeta<number>(db, 'lastVersion')) ?? 0
    const changes = await buildChanges(db, entries)

    const headers = senderConnectionId ? { [CONNECTION_ID_HEADER]: senderConnectionId } : undefined
    const response = await apiSend<SyncResponse>('POST', '/api/sync', { since, changes }, headers)
    await applyResponse(db, response, changes, snapshotMax)

    if (import.meta.env.DEV) {
      console.debug('[sync] done', {
        pushed: changes.length,
        pulled: response.tasks.length,
        rejected: response.rejected.length,
        version: response.version,
      })
    }
    return true
  } catch (error) {
    console.error('[sync] failed', { status: error instanceof ApiError ? error.status : undefined })
    if (import.meta.env.DEV) console.debug('[sync] failure detail', error)
    return false
  }
}

/**
 * One change per task, in first-change order. A task whose last entry is a delete sends only the delete (the
 * user's final intent; an own edit sent first would make the delete look stale). Otherwise the changed fields
 * of all entries are merged and sent with the row's current values.
 */
async function buildChanges(db: QuestsDb, entries: OutboxEntry[]): Promise<TaskChange[]> {
  const byTask = new Map<string, OutboxEntry[]>()
  for (const entry of entries) {
    const group = byTask.get(entry.taskId)
    if (group) group.push(entry)
    else byTask.set(entry.taskId, [entry])
  }

  const rows = await db.tasks.bulkGet([...byTask.keys()])
  const changes: TaskChange[] = []
  for (const [index, group] of [...byTask.values()].entries()) {
    const change = toChange(group, rows[index])
    if (change) changes.push(change)
  }
  return changes
}

function toChange(group: OutboxEntry[], row: Task | undefined): TaskChange | null {
  const last = group[group.length - 1]
  if (last.op === 'delete') {
    return {
      id: last.taskId, op: 'delete', baseVersion: last.baseVersion, changed: [],
      title: null, notes: null, dueAt: null, completedAt: null, createdAt: null,
    }
  }

  if (!row) return null // removed locally by a pulled tombstone before this push; nothing to send
  const changed = [...new Set(group.flatMap((e) => e.changed))]
  return {
    id: row.id, op: 'upsert', baseVersion: last.baseVersion, changed,
    title: row.title, notes: row.notes, dueAt: row.dueAt, completedAt: row.completedAt, createdAt: row.createdAt,
  }
}

/**
 * Applies a successful response in one transaction: pulled tasks replace local rows (a tombstone removes the row),
 * except fields of changes made during the request, which keep their local values. Then clears the pushed outbox
 * entries and stores the new version.
 */
async function applyResponse(db: QuestsDb, response: SyncResponse, pushed: TaskChange[], snapshotMax: number) {
  await db.transaction('rw', db.tasks, db.outbox, db.meta, async () => {
    const pending = pendingFieldsByTask(await db.outbox.where('seq').above(snapshotMax).toArray())

    for (const task of response.tasks) await applyPulledTask(db, task, pending.get(task.id))

    // A pushed delete that the server applied (or found nothing to delete) is not returned on a first pull or for
    // a task the server never had: drop the local tombstone row.
    const returned = new Set(response.tasks.map((t) => t.id))
    const settledDeletes = pushed
      .filter((c) => c.op === 'delete' && !returned.has(c.id) && !pending.has(c.id))
      .map((c) => c.id)
    await db.tasks.bulkDelete(settledDeletes)

    await db.outbox.where('seq').belowOrEqual(snapshotMax).delete()
    await writeMeta(db, 'lastVersion', response.version)
  })
}

function pendingFieldsByTask(entries: OutboxEntry[]): Map<string, Set<TaskField>> {
  const pending = new Map<string, Set<TaskField>>()
  for (const entry of entries) {
    const fields = pending.get(entry.taskId) ?? new Set<TaskField>()
    entry.changed.forEach((f) => fields.add(f))
    pending.set(entry.taskId, fields)
  }
  return pending
}

async function applyPulledTask(db: QuestsDb, pulled: Task, pendingFields: Set<TaskField> | undefined) {
  if (!pendingFields) {
    if (pulled.deletedAt !== null) await db.tasks.delete(pulled.id)
    else await db.tasks.put(pulled)
    return
  }

  const local = await db.tasks.get(pulled.id)
  if (!local) {
    await db.tasks.put(pulled)
    return
  }

  // Local edits made during the request win over the echo; the local delete state too.
  const merged: Task = { ...pulled, deletedAt: local.deletedAt }
  const fields: Record<TaskField, string | null> = merged
  for (const field of pendingFields) fields[field] = local[field]
  await db.tasks.put(merged)
}
