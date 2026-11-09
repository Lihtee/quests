import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { QuestsDb } from '../../shared/db'
import { setSyncConnectionId, syncNow } from './syncEngine'
import { startSyncSignal } from './syncSignal'

/** Minimal stand-in for a SignalR HubConnection. */
class FakeConnection {
  static created = 0
  state = 'Disconnected'
  connectionId: string | null = null
  readonly handlers = new Map<string, () => void>()
  reconnected: (() => void) | null = null

  readonly start = vi.fn(async () => {
    this.state = 'Connected'
    this.connectionId = `conn-${++FakeConnection.created}`
  })

  readonly stop = vi.fn(async () => {
    this.state = 'Disconnected'
    this.connectionId = null
  })

  on(name: string, handler: () => void) {
    this.handlers.set(name, handler)
  }
  onreconnecting() {}
  onreconnected(handler: () => void) {
    this.reconnected = handler
  }
  onclose() {}
}

const hub = vi.hoisted(() => ({ connection: null as unknown }))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Disconnected: 'Disconnected', Connected: 'Connected' },
  LogLevel: { Warning: 3, None: 6 },
  HubConnectionBuilder: class {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return hub.connection
    }
  },
}))

vi.mock('../auth/firebase', () => ({ auth: { currentUser: null } }))

vi.mock('./syncEngine', () => ({ syncNow: vi.fn(async () => undefined), setSyncConnectionId: vi.fn() }))

const db = {} as QuestsDb
const sync = vi.mocked(syncNow)
const setId = vi.mocked(setSyncConnectionId)
let connection: FakeConnection
let page: EventTarget & { visibilityState: DocumentVisibilityState }

const flush = () => new Promise((resolve) => setTimeout(resolve, 0))

async function setVisibility(state: DocumentVisibilityState) {
  page.visibilityState = state
  page.dispatchEvent(new Event('visibilitychange'))
  await flush()
}

beforeEach(() => {
  connection = new FakeConnection()
  hub.connection = connection
  page = Object.assign(new EventTarget(), { visibilityState: 'visible' as DocumentVisibilityState })
  vi.stubGlobal('document', page)
  vi.stubGlobal('window', new EventTarget())
  vi.spyOn(console, 'debug').mockImplementation(() => undefined)
  sync.mockClear()
  setId.mockClear()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('startSyncSignal', () => {
  it('connects while visible, then sends its connection id and syncs to catch up', async () => {
    const stop = startSyncSignal(db)
    await flush()

    expect(connection.start).toHaveBeenCalledOnce()
    expect(setId).toHaveBeenLastCalledWith(connection.connectionId)
    expect(sync).toHaveBeenCalledWith(db)
    stop()
  })

  it('does not connect while hidden', async () => {
    page.visibilityState = 'hidden'

    const stop = startSyncSignal(db)
    await flush()

    expect(connection.start).not.toHaveBeenCalled()
    stop()
  })

  it('runs a sync when the server says something changed', async () => {
    const stop = startSyncSignal(db)
    await flush()
    sync.mockClear()

    connection.handlers.get('changed')!()

    expect(sync).toHaveBeenCalledOnce()
    stop()
  })

  it('disconnects when hidden and reconnects with a catch-up sync when visible again', async () => {
    const stop = startSyncSignal(db)
    await flush()
    sync.mockClear()

    await setVisibility('hidden')
    expect(connection.stop).toHaveBeenCalledOnce()
    expect(setId).toHaveBeenLastCalledWith(null)

    await setVisibility('visible')
    expect(connection.start).toHaveBeenCalledTimes(2)
    expect(setId).toHaveBeenLastCalledWith(connection.connectionId)
    expect(sync).toHaveBeenCalledOnce()
    stop()
  })

  it('syncs after an automatic reconnect', async () => {
    const stop = startSyncSignal(db)
    await flush()
    sync.mockClear()

    connection.reconnected!()

    expect(sync).toHaveBeenCalledOnce()
    stop()
  })

  it('cleanup stops the connection, clears the id and ignores later visibility changes', async () => {
    const stop = startSyncSignal(db)
    await flush()

    stop()
    await flush()
    expect(connection.stop).toHaveBeenCalledOnce()
    expect(setId).toHaveBeenLastCalledWith(null)

    await setVisibility('hidden')
    await setVisibility('visible')
    expect(connection.start).toHaveBeenCalledOnce()
  })
})
