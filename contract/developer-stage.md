# App lifecycle stage — `released` vs `developer`

> Normative. Bilateral (server ↔ hub). An app now has a lifecycle **stage**. Everything already
> agreed — entitlement, free/paid, lock + offer, subscribe/revoke — applies to **`released`** apps.
> A **`developer`** app is not yet launched: it exists only for devices marked as developers, and for
> them it carries **no gate at all**.

## The rule

- **`released`** (default; absent stage = released): the normal public app. Unchanged.
- **`developer`**: appears in the catalog **only** for developer-marked devices, and is delivered to
  them **unlocked** — no lock, no offer, no entitlement check. To everyone else the app **does not
  exist**.

The stage is enforced **server-side**, keyed on the device record — not on the client's catalog JSON.
A tampered/copied local catalog changes nothing: delivery looks the device up in the database.

## Boundary changes (server ↔ hub)

### 1. The catalog request now carries the device identity — **POST `/v1/catalog`**

The catalog stops being an anonymous `GET`. The hub **POSTs the same `{ claim, capabilities }`
envelope** it already sends to authorize/delivery/web (`ClaimFactory`).

Transport chosen: **POST body**, not a header. Rationale:
- Reuses the existing envelope verbatim — the catalog becomes the 4th endpoint with an identical
  request shape; no new serialization.
- Sends the full identity (`deviceId` + `license` + fingerprint), so the server may mark developers by
  `deviceId` (the hub-generated anchor) or by `license`, its choice, with no later contract change.
- A response that **varies per device must not be publicly cached**. `GET` + an identity header is a
  cache footgun (a shared proxy could serve one device's catalog to another); `POST` is the correct
  non-cacheable semantic.
- Privacy rule honoured — identity in the body, never a querystring.

Response by device:
- **No identity / ordinary device** → `released` apps only (today's behaviour).
- **Developer-marked device** → `released` + `developer` apps, the developer ones already **unlocked**
  (no `access.locked`, no offer).

**Cutover:** the server must accept `POST` on `/v1/catalog` before this ships (it may keep `GET` during
the transition). The hub and server roll out together.

### 2. Catalog item gains an optional `stage`

`"stage": "developer"` on developer apps (so the hub can paint a **DEV** badge); omitted or
`"released"` otherwise. Purely visual — install/open/subscribe logic is identical to a released app.

### 3. Delivery for a developer app

- **Developer device** → the developer app is served **unconditionally** (entitlement bypass).
- **Any non-developer device** asking for a developer app → **`404 unknown-app`**, *not* the
  `403 + offer` of a locked released app. A dev app is not for sale; it does not exist for the public.

## Hub half (what actually changes)

Almost no new logic: a developer app arrives unlocked, so the existing install/open/subscribe flow
already handles it. Concretely the hub only:
- sends the device identity when fetching the catalog (POST claim), and
- shows a **DEV** badge when `stage == "developer"` (it takes the tile's top-right badge slot; NEW/UPD
  are suppressed there so they don't overlap).
