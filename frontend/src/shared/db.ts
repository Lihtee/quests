import Dexie, { type EntityTable } from 'dexie'

/** Local copy of a task (matches the API's TaskDto). Dates are ISO 8601 strings. */
export interface Task {
  id: string
  title: string
  notes: string | null
  dueAt: string | null
  completedAt: string | null
  createdAt: string
  updatedAt: string
  /** Server version the device last saw; 0 until the first successful sync. */
  version: number
  /** Non-null means deleted (tombstone); hidden from the UI. */
  deletedAt: string | null
}

/** Task fields a change can carry (names match the API). */
export type TaskField = 'title' | 'notes' | 'dueAt' | 'completedAt'

/** A local change waiting to be pushed. Values are read from the task row at push time. */
export interface OutboxEntry {
  seq?: number
  taskId: string
  op: 'upsert' | 'delete'
  changed: TaskField[]
  /** The task's server version when the change was made. */
  baseVersion: number
  at: string
}

export interface MetaEntry {
  key: string
  value: unknown
}

/** Known `meta` keys: `lastVersion` (number, last pulled server version) and `me` (cached /api/me). */
export type MetaKey = 'lastVersion' | 'me'

export type QuestsDb = Dexie & {
  tasks: EntityTable<Task, 'id'>
  outbox: EntityTable<OutboxEntry, 'seq'>
  meta: EntityTable<MetaEntry, 'key'>
}

/** Opens the signed-in user's local database (`quests-<uid>`); kept after sign-out so pending changes survive. */
export function openUserDb(uid: string): QuestsDb {
  const db = new Dexie(`quests-${uid}`) as QuestsDb
  db.version(1).stores({
    tasks: 'id',
    outbox: '++seq, taskId',
    meta: 'key',
  })
  return db
}

export async function readMeta<T>(db: QuestsDb, key: MetaKey): Promise<T | undefined> {
  return (await db.meta.get(key))?.value as T | undefined
}

export async function writeMeta(db: QuestsDb, key: MetaKey, value: unknown): Promise<void> {
  await db.meta.put({ key, value })
}
