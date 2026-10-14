# The builder on GitHub Pages

The prototype (`prototype/index.html`) as a site you can add to a phone's home screen:
**https://lasseastrup.github.io/BuildingInterior/**

`.github/workflows/pages.yml` builds it (`node prototype/web/build.mjs _site`) and deploys it to GitHub Pages whenever
the page or this folder changes. The build:

- puts the page in a full document (`shell.html`) with what a home screen needs: the manifest
  (`manifest.webmanifest`), the icons (`icons/`), and the iOS tags (the name *Storey*, full screen under the status bar);
- serves three.js and polygon-clipping from the site, in the versions the page names (from `prototype/tools`'
  `node_modules`), instead of from jsDelivr, and stops if the installed versions differ;
- adds a service worker (`sw.js`) that keeps the page, the libraries and the icons, so the builder opens from the home
  screen with no network. The page comes from the network when there is one, so a new version shows at once.

`prototype/index.html` itself is unchanged: opened on its own it still loads from jsDelivr, and the artifact publishes it
as before. The build stamps the site's version into the page (`window.STOREY_BUILD`); the benchmark report names it.

## Frame rate on a phone

The bar-chart button opens the city stats: **Benchmark** flies a fixed path over the 1,000-building test city (about
90 seconds, hands off the screen), then orbits one view under several settings, and opens a report with frame-rate,
interval percentiles, slow frames (over 20 ms), main-thread time, draw load and the resolution drawn per part. **Copy
report** puts it on the clipboard. **Performance…** holds the render settings for this browser: resolution (**Auto**, the
default, lowers it a step when frames come later than 60 fps while the main thread had time to spare, and raises it
again after 3 s without; or a fixed share), pixel ratio cap, lighting (full PBR, or diffuse only: the default on a touch
screen), shadows and their edges (16 taps, or 9 on a touch screen), MSAA, and a frame HUD. Low Power Mode caps Safari
at 30 fps, so turn it off before measuring.

## Setting it up, once

1. Settings › Pages › Build and deployment › Source: **GitHub Actions**.
2. If the deploy job says the branch isn't allowed to deploy to `github-pages`: Settings › Environments › github-pages ›
   Deployment branches and tags, add the branch (this work is on `claude/amazing-thompson-dgsbo8`).
3. Actions › *Builder on GitHub Pages*: re-run the last run, or push a change to the page.

## On a phone

- iPhone or iPad: open the address in Safari, then Share › **Add to Home Screen**.
- Android: open it in Chrome, then ⋮ › **Add to Home screen** (or *Install app*).

On iOS the home-screen builder keeps its own saved layout, apart from Safari's.

## Icons

`icons/icon.svg` is the source. `node prototype/web/icons.mjs` renders the PNGs: 180 px for iOS, 192 and 512 px for the
manifest (the 512 also as its maskable icon: the mark is inside the middle 80%), and 32 px for a browser tab.

## Checking it locally

```bash
(cd prototype/tools && npm install)
node prototype/web/build.mjs /tmp/site/BuildingInterior
python3 -m http.server -d /tmp/site 8000      # then http://localhost:8000/BuildingInterior/
```
