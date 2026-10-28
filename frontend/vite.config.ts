import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { readFileSync, readdirSync } from 'node:fs';

const pdfRoot = dirname(createRequire(import.meta.url).resolve('pdfjs-dist/package.json'));

export default defineConfig({
  cacheDir: 'node_modules/.vite',
  plugins: [react(), {
    name: 'bms-local-pdf-assets',
    generateBundle() {
      // Versioned local assets: no CDN, and predecessor workers retain their
      // own resources after deployment. Never overwrite another PDF.js version.
      for (const directory of ['cmaps', 'standard_fonts', 'wasm', 'iccs']) {
        for (const file of readdirSync(join(pdfRoot, directory), { withFileTypes: true })) {
          if (!file.isFile()) continue;
          const name = file.name.startsWith('LICENSE') ? file.name + '.txt' : file.name;
          this.emitFile({ type: 'asset', fileName: `assets/pdfjs-6.4.299/${directory}/${name}`,
            source: readFileSync(join(pdfRoot, directory, file.name)) });
        }
      }
      this.emitFile({ type: 'asset', fileName: 'assets/pdfjs-6.4.299/LICENSE.txt', source: readFileSync(join(pdfRoot, 'LICENSE')) });
    },
  }],
  server: {
    port: 5178,
    proxy: {
      '/api': 'http://localhost:5188',
      '/health': 'http://localhost:5188',
    },
  },
});
