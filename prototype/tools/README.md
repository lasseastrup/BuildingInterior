# Prototype tools

Headless harnesses over `../index.html`, run with Node and Playwright's Chromium.

```bash
npm install
npm run fixtures     # writes unity/Fixtures/{demo,variants,derived}.json for the C# tests
```

Set `CHROMIUM=/path/to/chrome` if Playwright should not use its own browser download.
