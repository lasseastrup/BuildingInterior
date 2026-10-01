# Prototype tools

Headless harnesses over `../index.html`, run with Node and Playwright's Chromium.

```bash
npm install
npm run fixtures     # writes unity/Fixtures/{demo,variants,derived}.json for the C# tests
npm run ops          # writes unity/Fixtures/ops.json: the edit operations' answers (docs/EDITOR.md)
npm run check:palette   # the prototype opens Unity-written layouts: palette ids, per-style colours (docs/COLOURS.md)
```

Set `CHROMIUM=/path/to/chrome` if Playwright should not use its own browser download.
