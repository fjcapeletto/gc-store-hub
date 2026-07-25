# Catalog endpoint — hub → backend request

> Hub (`gc-store-hub`) request to `gc-store-server`. Bilateral. What the server must add so the
> production hub can render the shelf. Wire shape references `catalog.schema.json`.

## Why (current blocker)

Production hub **v0.0.2** reaches `POST /v1/authorize` fine (connectivity LED green, access/request
flow works), but has **no catalog source**: every catalog path on the server 404s
(`/v1/catalog`, `/catalog`, `/catalog.json`, … all `404`). So the hub can't fetch the apps list and
shows **"Store unavailable"** (its `Catalog:Url` is still the committed placeholder
`REPLACE-ME.example.com`, which is why the DNS error).

In the **one-server model** the shelf must come from `gcstore.gabrielcapeletto.com` too. The catalog
is **public** — the shelf is visible to every device; *installability* is what `/v1/authorize`
gates. So: two calls to the one server — `GET /v1/catalog` (shelf, public) + `POST /v1/authorize`
(access + heartbeat, device-scoped).

## What to build

### 1. Endpoint
- **`GET /v1/catalog`** (path is your call — hub is agnostic; just tell me the final URL).
- **Public / unauthenticated.** No claim, no device scoping — the shelf is identical for everyone.
- HTTPS, same host.

### 2. Response — `200 OK`
- **`Content-Type: application/json; charset=utf-8`** (the hub deserializes with
  `System.Net.Http.Json`; a non-JSON content type will make it reject the body).
- Body = the catalog manifest per [`catalog.schema.json`](catalog.schema.json), camelCase,
  kebab-case enums:

```json
{
  "catalogVersion": 1,
  "apps": [
    {
      "id": "com.gc.weather",
      "name": "Weather",
      "version": "1.3.0",
      "summary": "Local forecast at a glance.",
      "icon": "cloud",
      "identityMode": "device-only",
      "protection": "native"
    }
  ]
}
```

Per-app fields:

| field | req? | notes |
|-------|------|-------|
| `id` | **yes** | stable appId, immutable across versions (e.g. `com.gc.weather`). **Must match the `app_id`s you provision in `device_entitlement`** or grants won't line up in the hub. |
| `name` | **yes** | shelf label |
| `version` | **yes** | published version string; hub compares to installed to offer updates |
| `summary` | no | one-liner |
| `icon` | no | glyph key: `cloud` `notes` `cpu` `database` `photo` `music` `calendar` |
| `identityMode` | no | `device-only` \| `account-required` \| `hybrid` (default `device-only`) |
| `protection` | no | `sdk` \| `native` (default `native`) |

### 3. Do NOT put access in the catalog
- **Omit `access`/`install`** from the catalog response. In the split model the shelf is pure
  content; **access (`granted`/`locked`/`access-pending`) is decided per device by `/v1/authorize`**,
  and the hub applies it on top. (The hub ignores any `access` in the catalog when an entitlement
  endpoint is configured.)
- Hub-side follow-up: I'll relax `catalog.schema.json` to make `access` optional (it's currently
  `required`, a leftover from before the split) so an access-less catalog validates.

### 4. Content for the test
Serve the same apps as the mock so the E2E grant test lights up, with **appIds matching the
entitlements you provision**: `com.gc.weather`, `com.gc.notes`, `com.gc.sensorbridge`,
`com.gc.backup`, `com.gc.photos`, `com.gc.music`, `com.gc.calendar` (icons/identityMode as in
`mock/catalog.json`).

### 5. Nice-to-have (not blocking)
- **ETag / `If-None-Match` → `304 Not Modified`** — the hub polls the catalog (launch + focus +
  timer); a 304 makes idle polls cheap. Not required now; the hub re-downloads the full JSON today.
- Bump `catalogVersion` whenever the catalog changes.

### 6. Errors
- Server-side failure → any `5xx`; the hub falls back to its last-known cached catalog and shows an
  offline notice. No auth errors (it's public).

## Acceptance criteria (done = all true)
1. `curl -s https://gcstore.gabrielcapeletto.com/v1/catalog` → `200`, `application/json`, a manifest
   validating against `catalog.schema.json`.
2. The apps' `id`s match the `device_entitlement.app_id`s you grant.
3. With the hub's `Catalog:Url` pointed at that URL, the shelf renders the apps.

## What the hub does once it's live (no server dependency on this)
- Point `Catalog:Url` at the endpoint (via `GCSTORE_Catalog__Url` env for testing now; committed
  `appsettings.json` in the next release).
- Relax `catalog.schema.json` (`access` optional).
- Fix a hub UX gap so a catalog-down state doesn't dead-end to "Store unavailable" while the access
  flow (🔑) is reachable.

Ping me with the final URL and I re-point + we run the E2E: 🔑 request → you approve + mint license
for the new `device_id` → paste license → apps unlock.
