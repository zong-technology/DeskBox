import { build } from 'esbuild';
await build({ entryPoints: ['public/hologram.js'], bundle: true, format: 'esm', target: 'chrome120', outfile: 'public/hologram.bundle.js', minify: true });
console.log('Built holographic renderer');
