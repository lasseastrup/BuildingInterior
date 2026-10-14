// The builder's service worker (prototype/web/README.md). It keeps the page, its libraries and its icons, so the builder
// opens from the home screen with no network. The page comes from the network when there is one, so a new version shows
// at once; the rest is kept per version of the site (build.mjs stamps the version below), and the fonts once fetched.
const VERSION = '__VERSION__';
const SITE = 'storey-builder-' + VERSION, FONTS = 'storey-builder-fonts';
const FILES = ['./', 'manifest.webmanifest', 'vendor/three.module.min.js', 'vendor/polygon-clipping.umd.min.js',
  'icons/icon.svg', 'icons/favicon-32.png', 'icons/apple-touch-icon.png', 'icons/icon-192.png', 'icons/icon-512.png'];

self.addEventListener('install', e => {
  e.waitUntil(caches.open(SITE).then(c => c.addAll(FILES)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', e => {
  // the versions before this one go
  e.waitUntil(caches.keys()
    .then(keys => Promise.all(keys.filter(k => k.startsWith('storey-builder-') && k !== SITE && k !== FONTS).map(k => caches.delete(k))))
    .then(() => self.clients.claim()));
});

self.addEventListener('fetch', e => {
  const r = e.request;
  if (r.method !== 'GET') return;
  const u = new URL(r.url);
  if (r.mode === 'navigate') {
    // the page: the network's, kept for when there is none
    e.respondWith(fetch(r)
      .then(res => { if (res.ok) { const copy = res.clone(); caches.open(SITE).then(c => c.put('./', copy)); } return res; })
      .catch(() => caches.match('./')));
  } else if (u.origin === location.origin) {
    e.respondWith(caches.match(r).then(hit => hit || fetch(r)));
  } else if (u.hostname === 'fonts.googleapis.com' || u.hostname === 'fonts.gstatic.com') {
    e.respondWith(caches.open(FONTS).then(c => c.match(r).then(hit => hit || fetch(r).then(res => {
      if (res.ok || res.type === 'opaque') c.put(r, res.clone());
      return res;
    }))));
  }
});
