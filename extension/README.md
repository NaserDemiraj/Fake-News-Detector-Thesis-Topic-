# VerifyNews Browser Extension

A Manifest V3 browser extension that analyses the current page **or any selected text** for
fake news, bias, and misinformation with one click, using the VerifyNews backend.

By default it talks to the **live deployed backend** (`https://naserd-fake-news-backend.hf.space`),
so it works out of the box — no local server needed.

## Loading it in Chrome / Edge

1. Open `chrome://extensions` (or `edge://extensions`).
2. Toggle **Developer mode** on (top-right).
3. Click **Load unpacked** and select this `extension/` folder.
4. Pin the VerifyNews icon, open any article, and click **Analyse this page** — or highlight
   a paragraph and click **Analyse selected text**.

## Loading it in Firefox

1. Open `about:debugging#/runtime/this-firefox`.
2. Click **Load Temporary Add-on** and select `manifest.json` inside this folder.

## Configuration

Edit [`config.js`](config.js):

```js
const BACKEND_URL = 'https://naserd-fake-news-backend.hf.space'; // live API (default)
const APP_URL = 'https://fake-news-detector-thesis-topic.vercel.app';
```

To run against a local backend instead, set these to `http://localhost:5000` /
`http://localhost:3000` and add `"http://localhost:5000/*"` to `host_permissions` in
[`manifest.json`](manifest.json).

The backend already permits `chrome-extension://` and `moz-extension://` origins via CORS
(authentication is Bearer-token based, so this is safe).

## Notes

- Analyses run **anonymously** (no login required). They are still saved server-side and
  benefit from content-hash caching, so re-analysing the same page is instant.
- Icons are intentionally omitted for the dev build — the browser shows a default icon.
  Add `icon16/48/128.png` and an `icons` block to `manifest.json` before publishing.
