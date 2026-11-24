/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Tests run in a zone different from the ward (+03:00) to prove the board shows ward time, not browser time.
if (process.env.VITEST) {
  process.env.TZ = 'America/New_York';
}

// The board talks to Baton.Api (dotnet run --project src/Baton.Api --launch-profile inmemory) on port 5188.
const api = 'http://localhost:5188';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': api,
      '/health': api,
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
  },
});
