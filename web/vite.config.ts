import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const apiUrl = process.env.API_URL ?? 'http://localhost:5151'

export default defineConfig({
  plugins: [react()],
  server: {
    // Same-origin calls to /api in dev, so the API needs no CORS configuration.
    proxy: { '/api': apiUrl },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/setupTests.ts',
  },
})
