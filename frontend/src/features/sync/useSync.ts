import { useEffect } from 'react'
import type { QuestsDb } from '../../shared/db'
import { cancelRequestedSync, syncNow } from './syncEngine'
import { startSyncSignal } from './syncSignal'

const INTERVAL_MS = 60_000

/**
 * Runs sync while signed in: on start, window focus, coming back online, every minute while the page is
 * visible (no background timer, for battery), and when the SignalR doorbell rings (see syncSignal).
 * Local writes trigger their own debounced sync.
 */
export function useSync(db: QuestsDb): void {
  useEffect(() => {
    const run = () => void syncNow(db)
    const runIfVisible = () => {
      if (document.visibilityState === 'visible') run()
    }

    run()
    const stopSignal = startSyncSignal(db)
    window.addEventListener('focus', run)
    window.addEventListener('online', run)
    const interval = setInterval(runIfVisible, INTERVAL_MS)

    return () => {
      window.removeEventListener('focus', run)
      window.removeEventListener('online', run)
      clearInterval(interval)
      stopSignal()
      cancelRequestedSync(db)
    }
  }, [db])
}
