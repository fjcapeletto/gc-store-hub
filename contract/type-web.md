# `type: web` — pushed links, opened in the external browser

> Concrete contract for the `web` app type (see [`app-types.md`](app-types.md)). First real thin app:
> **GC Deals** — a subscribe-able stream of marketplace/offer links. The hub renders **no content**;
> it pushes links and hands them to the user's **default browser**. Bilateral; rides the shared
> entitlement spine.

## Behaviour (GC Deals)

- A shelf item with an icon; its catalog `type` is **`web`**.
- The tile button is **Subscribe / Unsubscribe** (not Install/Open).
- **Free to everyone** by default, but **revocable** per device (to exclude a customer). Two "off"
  states, both real:
  - **Unsubscribe** = the *customer's* choice (client-side: stop polling/notifying, clear the inbox).
  - **Revoke** = *our* choice (server-side: delivery `403`s that device even if subscribed). ← the E2E revoke.
- New link published → the hub raises a **systray toast**. Click the toast → the link opens in the
  **external browser** (already installed).
- Click the **app tile** (not the toast) → a small **inbox**: the recent links; click one → external browser.

## Wire

### Catalog
The item carries `type: "web"`. No package, no launch info — a `web` app has no local install.

### Delivery = a gated poll for the link-list
`POST /v1/delivery/{appId}` with the same `{ claim, capabilities }` as authorize. For a `web` app the
server returns the current **link-list** (not a package):

```json
{
  "appId": "com.gc.deals",
  "type": "web",
  "items": [
    { "id": "d-1042", "title": "Refurb ThinkPad — 30% off (eBay)", "url": "https://www.ebay.com/itm/…", "publishedAt": "2026-07-26T14:00:00Z" },
    { "id": "d-1041", "title": "Back-to-school bundle (Walmart)",   "url": "https://www.walmart.com/ip/…", "publishedAt": "2026-07-25T18:00:00Z" }
  ]
}
```

- The hub **polls this on a cadence** (see below) — this *is* the "push" (pull + local toast; no WNS).
- `item.url` is any external URL (marketplace or GC's own site). `id` is stable per link (the hub uses
  it to detect what's new and to de-dupe the inbox).
- **Ungranted / revoked** device → `403 { reason, offer }` (as everywhere). That is the revoke path.
- The server may return only the recent N items; the hub keeps its own inbox history.

### Subscription
Client-side in v0: subscribing = the hub starts polling + notifying; unsubscribing = it stops and
clears local state. The server gates by **entitlement** (revoke), independent of the client's
subscribe choice. *(Optional later: the hub POSTs subscribe/unsubscribe so the server has the roster —
not required for the mechanics.)*

## Hub behaviour

- **Tile:** Subscribe / Unsubscribe. When subscribed, poll `/v1/delivery/{appId}` on the cadence.
- **New-item detection:** items whose `id` wasn't seen before → raise a **Windows systray toast**
  (title = `item.title`; activating it opens `item.url`). Requires an **AUMID** — available via the
  Start-Menu shortcut Velopack creates.
- **Inbox:** keep the last N items locally (id/title/url/publishedAt). Clicking the app tile opens the
  inbox list; clicking an item opens `item.url`.
- **Open a link:** `Process.Start(url)` with `UseShellExecute` → the user's default browser. No webview.
- **Revoke / unsubscribe:** on `403`, show revoked + stop; on unsubscribe, stop polling + clear.
- **Cadence & politeness:** poll on a sensible interval (e.g. the tray heartbeat cadence — slower in the
  tray), with ETag/`If-None-Match` when the server supports it, and backoff on failure. Don't hammer.

## What proves out (E2E)

Subscribe → server publishes a link → hub polls → **toast** → click → browser. Then **revoke** the
device → next poll `403` → the stream stops. Exercises entitlement + delivery + **revocation** +
notifications, with zero packaging.

## Server side (for the backend)

- Catalog: emit the GC Deals item with `type: "web"`.
- `POST /v1/delivery/com.gc.deals` → the gated `items` list above; `403 + offer` for revoked devices.
- A way to **publish a new link** (append to the list) so it shows up on the next poll.
- Entitlement: grant `com.gc.deals` broadly (free), revocable per device.
- Nice-to-have: ETag on the items list for cheap polls.
