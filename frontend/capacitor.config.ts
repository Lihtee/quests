import type { CapacitorConfig } from '@capacitor/cli'

// The React build is bundled into the APK (works offline). The default androidScheme (https) makes the
// WebView origin https://localhost; the server allows it via CORS_ORIGINS.
const config: CapacitorConfig = {
  appId: 'com.lihtee.quests',
  appName: 'Quests',
  webDir: 'dist',
  plugins: {
    FirebaseAuthentication: {
      // Native Google sign-in returns an ID token; the Firebase web SDK signs in with it (signInWithCredential).
      skipNativeAuth: true,
      providers: ['google.com'],
    },
  },
}

export default config
