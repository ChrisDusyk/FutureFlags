import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const server = process.env.SERVER_HTTPS || process.env.SERVER_HTTP;

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Proxy API calls to the app service
      '/api': {
        target: server,
        changeOrigin: true
      },
      // The OpenFeature Remote Evaluation Protocol lives outside /api, so it needs its own entry
      // or a browser-side provider gets index.html here and works only once deployed — the exact
      // shape of bug that hides until somebody tests against the real origin. changeOrigin matters
      // more here than above: it rewrites Host, and these routes read Origin to decide whether a
      // secret key has been shipped to a browser.
      '/ofrep': {
        target: server,
        changeOrigin: true
      }
    }
  }
});
