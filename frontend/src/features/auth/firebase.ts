import { initializeApp } from 'firebase/app'
import { connectAuthEmulator, getAuth } from 'firebase/auth'

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

const app = initializeApp({
  apiKey: env.VITE_FIREBASE_API_KEY,
  authDomain: env.VITE_FIREBASE_AUTH_DOMAIN,
  projectId: env.VITE_FIREBASE_PROJECT_ID,
  appId: env.VITE_FIREBASE_APP_ID,
})

export const auth = getAuth(app)

if (env.DEV && env.VITE_USE_AUTH_EMULATOR === '1') {
  console.debug('[firebase] using Auth Emulator', { url: authEmulatorUrl })
  connectAuthEmulator(auth, authEmulatorUrl)
}
