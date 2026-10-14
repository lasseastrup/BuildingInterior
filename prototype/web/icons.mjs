// Renders the builder's icons (icons/icon.svg) to the PNGs a home screen and the manifest ask for, with Playwright's
// Chromium (prototype/tools' playwright-core). Run it after changing icon.svg; the PNGs are kept in the repository.
//
//   node prototype/web/icons.mjs        (CHROMIUM=/path/to/chrome if Playwright should not use its own browser)
import fs from 'fs';
import path from 'path';
import { createRequire } from 'module';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const { chromium } = createRequire(path.join(here, '../tools/package.json'))('playwright-core');
const svg = fs.readFileSync(path.join(here, 'icons/icon.svg'), 'utf8');
// iOS takes the 180 px apple-touch-icon (opaque: it fills transparency with black); the manifest the 192 and 512 px ones
const sizes = { 'apple-touch-icon.png': 180, 'icon-192.png': 192, 'icon-512.png': 512, 'favicon-32.png': 32 };

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM || undefined });
const page = await browser.newPage({ deviceScaleFactor: 1 });
for (const [name, n] of Object.entries(sizes)) {
  await page.setViewportSize({ width: n, height: n });
  await page.setContent(`<!doctype html><html><body style="margin:0"><img width="${n}" height="${n}" style="display:block" src="data:image/svg+xml;base64,${Buffer.from(svg).toString('base64')}"></body></html>`);
  await page.locator('img').evaluate(img => img.decode());
  await page.screenshot({ path: path.join(here, 'icons', name), clip: { x: 0, y: 0, width: n, height: n } });
  console.log('wrote icons/' + name);
}
await browser.close();
