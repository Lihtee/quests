import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Backend API (dotnet run --project backend/Quests.Api)
      '/api': 'http://localhost:5080',
    },
  },
})
