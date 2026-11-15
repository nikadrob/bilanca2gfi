import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev: Vite on :5173 proxies /api to the ASP.NET Core API on :5080.
// Build: output goes straight into the API's wwwroot, so `dotnet run` serves the whole app.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
  build: {
    outDir: '../src/CalixHelper.Api/wwwroot',
    emptyOutDir: true,
  },
});
