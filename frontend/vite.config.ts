/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Backend API (dotnet run --project backend/Quests.Api); ws for the SignalR hub at /api/hubs/sync
      '/api': { target: 'http://localhost:5080', ws: true },
    },
  },
  test: {
    environment: 'node',
    setupFiles: ['./src/test/setup.ts'],
  },
})
