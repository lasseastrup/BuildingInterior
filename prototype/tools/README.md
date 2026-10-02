# Prototype tools

Headless harnesses over `../index.html`, run with Node and Playwright's Chromium.

```bash
npm install
npm run fixtures     # writes unity/Fixtures/{demo,variants,derived}.json for the C# tests
npm run ops          # writes unity/Fixtures/ops.json, ops-interior.json and ops-facade.json: the edit operations' answers (docs/EDITOR.md)
npm run play         # writes unity/Fixtures/play.json: the walk model and occlusion, per query and per frame of scripted walks (docs/PLAY.md)
npm run check:palette   # the prototype opens Unity-written layouts: palette ids, per-style colours (docs/COLOURS.md)
```

Set `CHROMIUM=/path/to/chrome` if Playwright should not use its own browser download.
