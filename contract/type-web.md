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
    { "id": "d-1042", "title": "Refurb ThinkPad — 30% off (eBay)", "url": "https://www.ebay.com/itm/…", "publishedAt": "2026-07-26T14:00:00Z", "imageUrl": "https://gcstore.gabrielcapeletto.com/icons/preview/com.gc.deals/<sha256>.jpg" },
    { "id": "d-1041", "title": "Back-to-school bundle (Walmart)",   "url": "https://www.walmart.com/ip/…", "publishedAt": "2026-07-25T18:00:00Z" }
  ]
}
```

- The hub **polls this on a cadence** (see below) — this *is* the "push" (pull + local toast; no WNS).
- `item.url` is any external URL (marketplace or GC's own site). `id` is stable per link (the hub uses
  it to detect what's new and to de-dupe the inbox).
- `item.imageUrl` is an **optional** link-preview thumbnail for the toast. The **server** resolves the
  target's `og:image` (or the operator supplies one) at publish time, validates it, and **re-hosts it
  content-hashed on our own domain** — so `imageUrl` always points at gcstore, never the external CDN,
  and the client's IP never leaks to eBay/Amazon/etc. Same trust model as the app icon. Absent when no
  preview was available (no image, timeout) → text-only toast, unchanged. Cache rule = URL is the
  version (content-hashed ⇒ new image = new URL = refresh). Only the individual toast renders it; the
  grouped toast stays image-less.
- **Ungranted / revoked** device → `403 { reason, offer }` (as everywhere). That is the revoke path.
- The server may return only the recent N items; the hub keeps its own inbox history.

#### Scheduled delivery — optional `deliverAt`

An item may carry an optional `deliverAt` (ISO-8601), letting the server queue a post ahead of time:

```json
{ "id": "d-1050", "title": "Cyber Monday drop", "url": "https://…", "publishedAt": "2026-11-25T09:00:00Z", "deliverAt": "2026-11-30T13:00:00Z" }
```

- **Absent → deliver now** (present behaviour, unchanged).
- **In the future →** the hub treats the item as if it doesn't exist yet: **no toast, not in the
  inbox**. Once `deliverAt` has passed, a later poll surfaces it normally (toast per the mode + inbox).
- Unparseable `deliverAt` **fails open** (shown now) — the hub never hides content by accident.
- The hub honours this today (the field is optional; absent = present). Ratified bilaterally — the
  server emits `deliverAt` on scheduled posts.

### Subscription
Client-side in v0: subscribing = the hub starts polling + notifying; unsubscribing = it stops and
clears local state. The server gates by **entitlement** (revoke), independent of the client's
subscribe choice. *(Optional later: the hub POSTs subscribe/unsubscribe so the server has the roster —
not required for the mechanics.)*

## Hub behaviour

- **Tile:** Subscribe / Unsubscribe. When subscribed, poll `/v1/delivery/{appId}` on the cadence.
- **New-item detection:** items whose `id` wasn't seen before are "new". How they're surfaced is a
  **per-app, client-side delivery mode** (below). The seen-set is pruned to the ids still on the
  server's list, so it can't grow without bound.
- **Inbox:** keep the last N items locally (id/title/url/publishedAt). Clicking the app tile opens the
  inbox list; clicking an item opens `item.url`.

### Delivery modes (client-side, per app)

The customer chooses how a web app notifies, via a gear on the tile. The hub owns this — it is **not**
in the contract; the server just supplies the items.

- **One at a time · oldest → newest** *(default)* — each new item is its own toast, emitted from the
  oldest-unseen to the newest, one every **cadence** (configurable per app; see below). Nothing is
  coalesced, so when a toast later carries its own image no content is lost. If the device was off and
  comes back to several unseen, they drip out on the cadence (the first fires immediately).
- **One at a time · newest → oldest** — same, but most-recent first.
- **One grouped toast** — a single image-less toast listing every new title as a clickable line.
- **Don't notify** — no toasts; new items just accumulate in the inbox until the user opens the app.

An item is marked "seen" only when it is actually surfaced (or, for silent, on receipt), so a mode
that drips over time still resumes correctly after a restart.

**Cadence** — for the two one-at-a-time modes, the gap between toasts is configurable per app,
**30 s min, 1 h max, default 2 min**. Client-side only (persisted locally); not part of the wire.
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
