import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiSend } from '../../shared/api'
import { openUserDb, readMeta, type QuestsDb, type Task } from '../../shared/db'
import { completeTask, createTask, deleteTask, updateTask } from '../tasks/taskStore'
import { syncNow, type SyncResponse, type TaskChange } from './syncEngine'

vi.mock('../../shared/api', () => ({
  apiSend: vi.fn(),
  ApiError: class ApiError extends Error {
    readonly status: number
    constructor(status: number) {
      super(`status ${status}`)
      this.status = status
    }
  },
}))

// Debounced auto-sync is driven by the tests instead.
vi.mock('./syncEngine', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./syncEngine')>()),
  requestSync: vi.fn(),
}))

const send = vi.mocked(apiSend)
let db: QuestsDb
let dbCount = 0

interface SyncBody {
  since: number
  changes: TaskChange[]
}

const lastBody = () => send.mock.calls.at(-1)![2] as SyncBody
const respond = (response: SyncResponse) => send.mockResolvedValueOnce(response)
const local = async (id: string) => await db.tasks.get(id)
const asServer = (task: Task, overrides: Partial<Task>): Task => ({ ...task, ...overrides })

/** Creates a task and syncs it so the server has it at `version`. */
async function syncedTask(title: string, version = 1): Promise<Task> {
  const id = await createTask(db, { title, notes: null, dueAt: null })
  const created = (await local(id))!
  respond({ version, tasks: [asServer(created, { version })], rejected: [] })
  await syncNow(db)
  return (await local(id))!
}

beforeEach(() => {
  db = openUserDb(`test-${++dbCount}`)
  send.mockReset()
  vi.spyOn(console, 'error').mockImplementation(() => undefined)
  vi.spyOn(console, 'debug').mockImplementation(() => undefined)
})

afterEach(async () => {
  vi.restoreAllMocks()
  await db.delete()
})

describe('syncNow', () => {
  it('pushes only the changed fields with the base version, then clears the outbox and stores the version', async () => {
    const task = await syncedTask('Task', 3)
    expect(lastBody().since).toBe(0)
    expect(await db.outbox.count()).toBe(0)
    expect(await readMeta<number>(db, 'lastVersion')).toBe(3)

    await completeTask(db, task.id)
    const completedAt = (await local(task.id))!.completedAt
    respond({ version: 4, tasks: [asServer(task, { completedAt, version: 4 })], rejected: [] })
    await syncNow(db)

    const body = lastBody()
    expect(body.since).toBe(3)
    expect(body.changes).toHaveLength(1)
    expect(body.changes[0]).toMatchObject({ id: task.id, op: 'upsert', changed: ['completedAt'], baseVersion: 3, completedAt })
    expect(await db.outbox.count()).toBe(0)
    expect(await readMeta<number>(db, 'lastVersion')).toBe(4)
    expect((await local(task.id))!.version).toBe(4)
  })

  it('merges several local edits of one task into one change', async () => {
    const task = await syncedTask('Old')
    await updateTask(db, task.id, { title: 'New', notes: null, dueAt: null })
    await updateTask(db, task.id, { title: 'New', notes: 'notes', dueAt: null })
    respond({ version: 3, tasks: [], rejected: [] })

    await syncNow(db)

    expect(lastBody().changes).toEqual([
      expect.objectContaining({ id: task.id, op: 'upsert', changed: ['title', 'notes'], title: 'New', notes: 'notes' }),
    ])
  })

  it('removes the local row when a tombstone is pulled', async () => {
    const task = await syncedTask('Task')
    respond({ version: 2, tasks: [asServer(task, { version: 2, deletedAt: '2026-11-09T10:00:00Z' })], rejected: [] })

    await syncNow(db)

    expect(await local(task.id)).toBeUndefined()
  })

  it('brings a deleted task back when the server ignored the delete', async () => {
    const task = await syncedTask('Task')
    await deleteTask(db, task.id)
    expect((await local(task.id))!.deletedAt).not.toBeNull()
    respond({ version: 3, tasks: [asServer(task, { version: 2, notes: 'edited elsewhere' })], rejected: [] })

    await syncNow(db)

    expect(lastBody().changes).toEqual([expect.objectContaining({ id: task.id, op: 'delete', baseVersion: 1 })])
    const back = (await local(task.id))!
    expect(back.deletedAt).toBeNull()
    expect(back.notes).toBe('edited elsewhere')
    expect(await db.outbox.count()).toBe(0)
  })

  it('drops the local row of a deleted task once the server applied the delete', async () => {
    const task = await syncedTask('Task')
    await deleteTask(db, task.id)
    respond({ version: 2, tasks: [], rejected: [] }) // e.g. a first pull, which skips tombstones

    await syncNow(db)

    expect(await local(task.id)).toBeUndefined()
  })

  it('sends only the delete for a task created and deleted offline', async () => {
    const id = await createTask(db, { title: 'Temp', notes: null, dueAt: null })
    await deleteTask(db, id)
    respond({ version: 0, tasks: [], rejected: [] })

    await syncNow(db)

    expect(lastBody().changes).toEqual([expect.objectContaining({ id, op: 'delete', baseVersion: 0 })])
    expect(await local(id)).toBeUndefined()
  })

  it('keeps an edit made during the request over the pulled echo', async () => {
    const task = await syncedTask('Task')
    await updateTask(db, task.id, { title: 'Pushed', notes: null, dueAt: null })
    send.mockImplementationOnce(async () => {
      await updateTask(db, task.id, { title: 'Typed during sync', notes: null, dueAt: null })
      const response: SyncResponse = {
        version: 2,
        tasks: [asServer(task, { title: 'Pushed', notes: 'from other device', version: 2 })],
        rejected: [],
      }
      return response
    })

    await syncNow(db)

    const after = (await local(task.id))!
    expect(after.title).toBe('Typed during sync')
    expect(after.notes).toBe('from other device')
    expect(after.version).toBe(2)
    const pending = await db.outbox.toArray()
    expect(pending).toEqual([expect.objectContaining({ taskId: task.id, changed: ['title'] })])
  })

  it('keeps the outbox and local data when the network fails', async () => {
    const task = await syncedTask('Task')
    await updateTask(db, task.id, { title: 'Offline edit', notes: null, dueAt: null })
    send.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    await syncNow(db)

    expect(console.error).toHaveBeenCalledWith('[sync] failed', { status: undefined })
    expect(await db.outbox.count()).toBe(1)
    expect((await local(task.id))!.title).toBe('Offline edit')
    expect(await readMeta<number>(db, 'lastVersion')).toBe(1)
  })

  it('drops the entry of a rejected change', async () => {
    const task = await syncedTask('Task')
    await updateTask(db, task.id, { title: 'Edit', notes: null, dueAt: null })
    respond({ version: 2, tasks: [task], rejected: [{ id: task.id, reason: 'invalid' }] })

    await syncNow(db)

    expect(await db.outbox.count()).toBe(0)
    expect((await local(task.id))!.title).toBe('Task')
  })
})
