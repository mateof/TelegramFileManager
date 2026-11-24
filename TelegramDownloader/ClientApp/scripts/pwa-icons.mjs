// Generates the PWA icons of the web app from its brand mark (the folder with
// a paper plane of apps/web/public/favicon.svg and shell/Logo.tsx).
// The PNGs are committed; run this again only when the mark changes:
//
//   npm run pwa:icons        (from ClientApp)
//
// Output (apps/web/public/icons):
//   icon-192.png, icon-512.png          purpose "any": the rounded tile on transparent
//   maskable-192.png, maskable-512.png  purpose "maskable": full-bleed, mark inside the safe zone
//   apple-touch-icon.png (180)          opaque square, iOS rounds the corners itself
//   favicon-32.png                      PNG fallback of favicon.svg
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const out = fileURLToPath(new URL('../apps/web/public/icons/', import.meta.url));
mkdirSync(out, { recursive: true });

const gradient =
  '<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#818cf8"/><stop offset="1" stop-color="#4f46e5"/></linearGradient></defs>';
const glyph =
  '<path d="M15 22a5 5 0 0 1 5-5h9l4 5h11a5 5 0 0 1 5 5v15a5 5 0 0 1-5 5H20a5 5 0 0 1-5-5z" fill="#fff" fill-opacity=".92"/>' +
  '<path d="M27 33l12-5-4 12-3-4z" fill="#4f46e5"/>';

/** The tile with rounded corners (same as favicon.svg). */
const rounded = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">${gradient}<rect width="64" height="64" rx="16" fill="url(#g)"/>${glyph}</svg>`;

/** Full-bleed square, the glyph scaled around the centre (`scale` < 1 keeps it in the safe zone). */
const square = (scale) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">${gradient}<rect width="64" height="64" fill="url(#g)"/>` +
  `<g transform="translate(32 32) scale(${scale}) translate(-32 -32)">${glyph}</g></svg>`;

const jobs = [
  ['icon-192.png', rounded, 192],
  ['icon-512.png', rounded, 512],
  // Maskable safe zone: a centred circle of 80% of the size. The glyph is
  // ~34x30 units of 64; at 0.8 it spans ~43% of the icon.
  ['maskable-192.png', square(0.8), 192],
  ['maskable-512.png', square(0.8), 512],
  ['apple-touch-icon.png', square(0.95), 180],
  ['favicon-32.png', rounded, 32]
];

for (const [name, svg, size] of jobs) {
  await sharp(Buffer.from(svg), { density: Math.ceil((size / 64) * 72 * 2) })
    .resize(size, size)
    .png({ compressionLevel: 9 })
    .toFile(out + name);
  console.log(`${name} (${size}x${size})`);
}
