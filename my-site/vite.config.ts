import { defineConfig } from 'vite';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      // В dev фронт на :5173 проксирует /api на ASP.NET бэкенд
      '/api': {
        target: 'http://localhost:5183',
        changeOrigin: false,
      },
    },
  },
});
