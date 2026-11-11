import { Capacitor } from '@capacitor/core'
import { initializeApp } from 'firebase/app'
import {
  connectAuthEmulator,
  getAuth,
  indexedDBLocalPersistence,
  initializeAuth,
  type Auth,
} from 'firebase/auth'

declare global {
  interface ImportMetaEnv {
    readonly VITE_FIREBASE_API_KEY: string
    readonly VITE_FIREBASE_AUTH_DOMAIN: string
    readonly VITE_FIREBASE_PROJECT_ID: string
    readonly VITE_FIREBASE_APP_ID: string
    /** "1" = use the local Firebase Auth Emulator (dev only; see .env.emulator). */
    readonly VITE_USE_AUTH_EMULATOR?: string
    /** Absolute server URL for API and hub calls (Android build). Empty = relative `/api` (web). */
    readonly VITE_API_BASE_URL?: string
  }
}

const env = import.meta.env
const authEmulatorUrl = 'http://127.0.0.1:9099'
const isNative = Capacitor.isNativePlatform()

const app = initializeApp({
  apiKey: env.VITE_FIREBASE_API_KEY,
  authDomain: env.VITE_FIREBASE_AUTH_DOMAIN,
  projectId: env.VITE_FIREBASE_PROJECT_ID,
  appId: env.VITE_FIREBASE_APP_ID,
})

// In the Android WebView getAuth()'s popup/redirect resolver doesn't work; sign-in there goes through the native
// plugin + signInWithCredential, so auth is created without a resolver and persisted in IndexedDB.
export const auth: Auth = isNative
  ? initializeAuth(app, { persistence: indexedDBLocalPersistence })
  : getAuth(app)

if (env.DEV) console.debug('[firebase] auth initialized', { platform: Capacitor.getPlatform() })

if (env.DEV && !isNative && env.VITE_USE_AUTH_EMULATOR === '1') {
  console.debug('[firebase] using Auth Emulator', { url: authEmulatorUrl })
  connectAuthEmulator(auth, authEmulatorUrl)
}
