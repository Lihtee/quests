import { useEffect } from 'react'
import type { QuestsDb } from '../../shared/db'
import { cancelRequestedSync, syncNow } from './syncEngine'

const INTERVAL_MS = 60_000

/**
 * Runs sync while signed in: on start, window focus, coming back online, and every minute while the page is
 * visible (no background timer, for battery). Local writes trigger their own debounced sync.
 */
export function useSync(db: QuestsDb): void {
  useEffect(() => {
    const run = () => void syncNow(db)
    const runIfVisible = () => {
      if (document.visibilityState === 'visible') run()
    }

    run()
    window.addEventListener('focus', run)
    window.addEventListener('online', run)
    const interval = setInterval(runIfVisible, INTERVAL_MS)

    return () => {
      window.removeEventListener('focus', run)
      window.removeEventListener('online', run)
      clearInterval(interval)
      cancelRequestedSync(db)
    }
  }, [db])
}
