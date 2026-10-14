// Builds the builder's GitHub Pages site (prototype/web/README.md, .github/workflows/pages.yml): the prototype's page in a
// full document with what a home screen needs (a manifest, icons, a service worker so it opens with no network), and
// three.js and polygon-clipping served by the site itself, in the versions the page names, instead of from jsDelivr.
// prototype/index.html itself is left as it is.
//
//   node prototype/web/build.mjs [out]     (out: _site; needs prototype/tools' node_modules: npm ci there)
import fs from 'fs';
import path from 'path';
import crypto from 'crypto';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url)), proto = path.resolve(here, '..'), modules = path.join(proto, 'tools/node_modules');
const out = path.resolve(process.argv[2] || '_site');

// what the page loads from jsDelivr, and the file of the same version the site serves instead
const libraries = [
  { url: 'https://cdn.jsdelivr.net/npm/three@0.170.0/build/three.module.js', pkg: 'three', file: 'build/three.module.min.js', license: 'LICENSE' },
  { url: 'https://cdn.jsdelivr.net/npm/polygon-clipping@0.15.7/dist/polygon-clipping.umd.min.js', pkg: 'polygon-clipping', file: 'dist/polygon-clipping.umd.min.js', license: 'LICENSE.md' },
];

let page = fs.readFileSync(path.join(proto, 'index.html'), 'utf8');
const files = new Map();   // site path -> contents
for (const l of libraries) {
  const want = l.url.match(/@([^/]+)\//)[1], have = JSON.parse(fs.readFileSync(path.join(modules, l.pkg, 'package.json'), 'utf8')).version;
  if (want !== have) throw new Error(`the page loads ${l.pkg} ${want}, but prototype/tools has ${have} installed`);
  const n = page.split(l.url).length - 1;
  if (n !== 1) throw new Error(`the page names ${l.url} ${n} times, not once`);
  const as = 'vendor/' + path.basename(l.file);
  page = page.replace(l.url, () => './' + as);   // an import map's value must start with ./ (a bare one is ignored)
  files.set(as, fs.readFileSync(path.join(modules, l.pkg, l.file)));
  files.set(`vendor/${l.pkg}.LICENSE.txt`, fs.readFileSync(path.join(modules, l.pkg, l.license)));
}

const shell = fs.readFileSync(path.join(here, 'shell.html'), 'utf8'), marker = '<!-- the builder -->';
if (shell.split(marker).length !== 2) throw new Error('shell.html must hold ' + marker + ' once');
files.set('index.html', Buffer.from(shell.replace(marker, () => page)));
files.set('manifest.webmanifest', fs.readFileSync(path.join(here, 'manifest.webmanifest')));
for (const f of fs.readdirSync(path.join(here, 'icons'))) files.set('icons/' + f, fs.readFileSync(path.join(here, 'icons', f)));

// the service worker keeps this version of the site: a hash of everything else in it
const hash = crypto.createHash('sha256');
for (const [p, c] of [...files].sort((a, b) => a[0] < b[0] ? -1 : 1)) hash.update(p).update(c);
const version = hash.digest('hex').slice(0, 12);
files.set('sw.js', Buffer.from(fs.readFileSync(path.join(here, 'sw.js'), 'utf8').replace('__VERSION__', version)));

fs.rmSync(out, { recursive: true, force: true });
for (const [p, c] of files) { fs.mkdirSync(path.dirname(path.join(out, p)), { recursive: true }); fs.writeFileSync(path.join(out, p), c); }
console.log(`built ${out}: ${files.size} files, version ${version}`);
