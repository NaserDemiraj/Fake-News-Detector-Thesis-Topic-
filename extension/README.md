# VerifyNews Browser Extension

A Manifest V3 browser extension that analyses the current page for fake news, bias, and
misinformation with one click, using the VerifyNews backend.

## Loading it in Chrome / Edge (development)

1. Make sure the backend is running (`dotnet run` in `/backend`, listening on `http://localhost:5000`).
2. Open `chrome://extensions` (or `edge://extensions`).
3. Toggle **Developer mode** on (top-right).
4. Click **Load unpacked** and select this `extension/` folder.
5. Pin the VerifyNews icon, open any article, and click **Analyse this page**.

## Loading it in Firefox

1. Open `about:debugging#/runtime/this-firefox`.
2. Click **Load Temporary Add-on** and select `manifest.json` inside this folder.

## Configuration

Edit [`config.js`](config.js):

```js
const BACKEND_URL = 'http://localhost:5000'; // your API
const APP_URL = 'http://localhost:3000';     // your web app
```

When pointing at a deployed backend, also add that host to `host_permissions` in
[`manifest.json`](manifest.json), e.g. `"https://api.verifynews.app/*"`.

The backend already permits `chrome-extension://` and `moz-extension://` origins via CORS
(authentication is Bearer-token based, so this is safe).

## Notes

- Analyses run **anonymously** (no login required). They are still saved server-side and
  benefit from content-hash caching, so re-analysing the same page is instant.
- Icons are intentionally omitted for the dev build — the browser shows a default icon.
  Add `icon16/48/128.png` and an `icons` block to `manifest.json` before publishing.
