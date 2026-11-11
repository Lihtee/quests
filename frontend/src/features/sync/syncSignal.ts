import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { apiUrl } from '../../shared/api'
import type { QuestsDb } from '../../shared/db'
import { auth } from '../auth/firebase'
import { setSyncConnectionId, syncNow } from './syncEngine'

const HUB_PATH = '/api/hubs/sync'
const CHANGED_METHOD = 'changed'

function debug(message: string, detail?: unknown): void {
  if (import.meta.env.DEV) console.debug(`[signal] ${message}`, detail ?? '')
}

/**
 * SignalR "doorbell": while the page is visible, keeps a hub connection open and runs a sync when the server
 * says another device changed something. Disconnects when the page is hidden (battery). Every (re)connect runs
 * a sync to catch signals missed while disconnected. Returns a function that stops everything.
 */
export function startSyncSignal(db: QuestsDb): () => void {
  const connection = new HubConnectionBuilder()
    // withCredentials: false — the server's CORS policy does not allow credentials (bearer token only).
    .withUrl(apiUrl(HUB_PATH), {
      accessTokenFactory: async () => (await auth.currentUser?.getIdToken()) ?? '',
      withCredentials: false,
    })
    .withAutomaticReconnect()
    // SignalR's own logs can include the hub URL with the token; keep them to warnings in DEV only.
    .configureLogging(import.meta.env.DEV ? LogLevel.Warning : LogLevel.None)
    .build()

  let disposed = false
  let starting = false

  const catchUp = () => {
    setSyncConnectionId(connection.connectionId)
    void syncNow(db)
  }

  connection.on(CHANGED_METHOD, () => {
    debug('changed')
    void syncNow(db)
  })
  connection.onreconnecting((error) => {
    debug('reconnecting', error)
    setSyncConnectionId(null)
  })
  connection.onreconnected(() => {
    debug('reconnected')
    catchUp()
  })
  connection.onclose((error) => {
    debug('closed', error)
    setSyncConnectionId(null)
  })

  const start = () => {
    if (disposed || starting || document.visibilityState !== 'visible') return
    if (connection.state !== HubConnectionState.Disconnected) return

    starting = true
    connection
      .start()
      .then(
        () => {
          debug('started')
          catchUp()
        },
        // Harmless: the timer/focus syncs still run, and the next visibility or online event retries.
        (error: unknown) => debug('start failed', error),
      )
      .finally(() => {
        starting = false
      })
  }

  const stop = () => {
    setSyncConnectionId(null)
    connection.stop().then(
      () => debug('stopped'),
      (error: unknown) => debug('stop failed', error),
    )
  }

  const onVisibilityChange = () => (document.visibilityState === 'visible' ? start() : stop())

  document.addEventListener('visibilitychange', onVisibilityChange)
  window.addEventListener('online', start)
  start()

  return () => {
    disposed = true
    document.removeEventListener('visibilitychange', onVisibilityChange)
    window.removeEventListener('online', start)
    stop()
  }
}
