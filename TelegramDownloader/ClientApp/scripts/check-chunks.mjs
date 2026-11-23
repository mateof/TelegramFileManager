// Fails the build when the generated chunks import each other in a cycle.
// A cycle between chunks can leave a module undefined at startup (the TFM
// Player PWA 1.8.0 shipped a blank page because two vendor chunks did).
// Also prints the gzip size of the JS loaded at startup (entry + its static
// imports), to keep an eye on the < 250 KB budget.
//
// Usage: node scripts/check-chunks.mjs [assetsDir]   (default ../wwwroot/app/assets)
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { gzipSync } from 'node:zlib';

const dir = resolve(process.cwd(), process.argv[2] ?? '../wwwroot/app/assets');
const htmlPath = resolve(dir, '..', 'index.html');
const BUDGET_KB = 250;

const chunks = readdirSync(dir).filter((f) => f.endsWith('.js'));

// Static imports only: `import ... from "./x.js"` / `import "./x.js"`.
// Dynamic import() chunks load later and cannot deadlock startup.
const staticImport = /(?:import|export)\s*(?:[\w*{}\s,$]+from\s*)?["']\.\/([^"']+\.js)["']/g;

const graph = new Map();
for (const file of chunks) {
  const code = readFileSync(join(dir, file), 'utf8');
  graph.set(file, [...new Set([...code.matchAll(staticImport)].map((m) => m[1]))]);
}

const cycles = [];
const state = new Map(); // 1 = visiting, 2 = done
const stack = [];
function visit(node) {
  state.set(node, 1);
  stack.push(node);
  for (const next of graph.get(node) ?? []) {
    if (state.get(next) === 1) cycles.push([...stack.slice(stack.indexOf(next)), next].join(' -> '));
    else if (!state.get(next)) visit(next);
  }
  stack.pop();
  state.set(node, 2);
}
for (const node of graph.keys()) if (!state.get(node)) visit(node);

if (cycles.length > 0) {
  console.error('Circular imports between chunks:\n  ' + cycles.join('\n  '));
  process.exit(1);
}
console.log(`Chunk graph OK (${chunks.length} chunks, no static cycles)`);

// Initial JS = entry script(s) referenced by index.html + everything they import statically
let html = '';
try {
  html = readFileSync(htmlPath, 'utf8');
} catch {
  console.warn(`(no ${htmlPath}: skipping the initial size report)`);
  process.exit(0);
}
const entries = [...html.matchAll(/<script[^>]+type="module"[^>]+src="[^"]*\/assets\/([^"]+\.js)"/g)].map((m) => m[1]);
const initial = new Set();
const walk = (f) => {
  if (initial.has(f) || !graph.has(f)) return;
  initial.add(f);
  for (const next of graph.get(f)) walk(next);
};
entries.forEach(walk);
const preloads = [...html.matchAll(/<link[^>]+rel="modulepreload"[^>]+href="[^"]*\/assets\/([^"]+\.js)"/g)].map((m) => m[1]);
preloads.forEach(walk);

let raw = 0;
let gz = 0;
for (const f of initial) {
  const buf = readFileSync(join(dir, f));
  raw += buf.length;
  gz += gzipSync(buf, { level: 9 }).length;
}
const kb = (n) => (n / 1024).toFixed(1);
const css = readdirSync(dir).filter((f) => f.endsWith('.css'));
const cssGz = css.reduce((sum, f) => sum + gzipSync(readFileSync(join(dir, f)), { level: 9 }).length, 0);
console.log(`Initial JS: ${initial.size} file(s), ${kb(raw)} KB raw, ${kb(gz)} KB gzip (budget ${BUDGET_KB} KB); CSS ${kb(cssGz)} KB gzip`);
if (gz / 1024 > BUDGET_KB) {
  console.error(`Initial JS is over the ${BUDGET_KB} KB gzip budget`);
  process.exit(1);
}
