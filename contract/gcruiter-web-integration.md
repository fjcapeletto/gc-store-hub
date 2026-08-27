# GCruiter web — integration guide (monorepo + web auth)

> Hand-off for the GCruiter team: how to launch the **web surface** (browser, no hub) for
> `https://gcruiter.gcstore.gabrielcapeletto.com`, gated by customer login. Companion to
> [`web-auth-flow.draft.md`](web-auth-flow.draft.md) (the full flow),
> [`type-api.draft.md`](type-api.draft.md) (the token), and
> [`entitlement-and-scope.draft.md`](entitlement-and-scope.draft.md) (axes + scope).

## The web auth flow (short — full version in `web-auth-flow.draft.md`)

```
Browser (GCruiter SPA) --(1) OIDC login--> Customer Keycloak (id.gcstore...)
Browser <--(2) KC token--
Browser --(3) POST /v1/delivery/gcstore.gcruiter (present KC token)--> gcstore
Browser <--(4) { apiBaseUrl, token, expiresAt } (short Ed25519 app token)--
Browser --(5) GET/POST /api/... (Authorization: Bearer <app token>)--> GCruiter backend
GCruiter backend --(6) verify app token via gcstore /v1/keys--> serve
```

- The **backend never talks to Keycloak.** It only verifies the **gcstore** token (`/v1/keys`) — exactly
  as it already does for the hub flow.
- **Silent refresh:** when the app token nears expiry (or `/api` returns `401`), the SPA re-fetches a
  fresh app token from gcstore. If its KC access token also expired, it refreshes that **silently** (KC
  refresh token / `prompt=none`) — **no re-login** while the KC session is alive.

## Concrete config (already set up on our side)

**Customer Keycloak** — `https://id.gcstore.gabrielcapeletto.com`
- Realm: `customers`
- Issuer: `https://id.gcstore.gabrielcapeletto.com/realms/customers`
- Client: **`gcruiter-web`** — public (no secret), **PKCE S256**, standard flow ON
- Valid redirect URIs: `https://gcruiter.gcstore.gabrielcapeletto.com/*`
- Web origins: `https://gcruiter.gcstore.gabrielcapeletto.com`

**gcstore** — `https://gcstore.gabrielcapeletto.com`
- App id: `gcstore.gcruiter`
- Token broker: `POST /v1/delivery/gcstore.gcruiter` → `{ apiBaseUrl, token, expiresAt }`
- Verify keys (JWKS): `GET /v1/keys`

## The Keycloak → delivery wire (RESOLVED — implemented on gcstore)

The SPA passes the Keycloak token to `/v1/delivery/{appId}` as a **plain bearer header**:

```
POST /v1/delivery/<appId>
Authorization: Bearer <Keycloak access token>
```

No claim envelope, no body needed on this path. gcstore validates the token against the `customers`
realm JWKS, resolves the account's entitlement, and answers one of:

- **200** — entitled:
  ```json
  { "appId": "gcstore.gcruiter", "type": "api", "apiBaseUrl": "https://gcruiter.gcstore…",
    "token": "<Ed25519 app token>", "scope": "app:gcstore.gcruiter app:gcstore.gcruiter:premium",
    "expiresAt": "2026-08-12T18:05:00Z" }
  ```
  `expiresAt` is **RFC3339 / ISO** (not epoch). `token` is the same Ed25519 the backend already verifies
  via `/v1/keys`. `scope` is **also a claim inside the token**, surfaced here so the SPA needn't decode it.
- **403** — **locked** (registered/valid customer, but no entitlement for this app):
  ```json
  { "reason": "locked",
    "offer": { "planKey": "gcruiter.premium", "title": "GCruiter Premium",
               "description": "…", "priceCents": <amount>, "currency": "USD",
               "ctaLabel": "Unlock", "checkoutUrl": "https://…/checkout?plan=gcruiter.premium" } }
  ```
  **`offer` schema** (all optional but `title`; render what is present): `planKey`, `title`,
  `description`, `priceCents` (integer, minor units), `currency` (ISO 4217), `ctaLabel` (button text),
  `checkoutUrl`. It is built from the plan the app names as its offer; if none is configured you get a
  bare `{ "title": "…", "ctaLabel": "Unlock" }`. `reason` is `locked` today (the only no-access case);
  `401` = invalid/absent customer token; `404` = unknown/non-`api` app. `checkoutUrl` may be empty until
  the purchase flow is wired.

## What the GCruiter FE (SPA) does

1. OIDC **auth-code + PKCE** login against the `customers` realm (`gcruiter-web` client).
2. Call `POST /v1/delivery/<appId>` with `Authorization: Bearer <KC token>`.
   - **403 `locked`** → render the **lock / paywall UI** (use `offer`); do not enter the app.
   - **200** → keep the `token` + `scope` and enter.
3. Call `/api/...` (same origin) with `Authorization: Bearer <app token>`.
4. Read **`scope`** (claim in the app token) to show/hide tiered features (see below).
5. Refresh the app token proactively (timer) and/or on `401` — silently, no re-login.

## What the GCruiter backend does

1. Serve the SPA static + `/api` (Spring, `index.html` fallback for SPA routing).
2. **Verify the gcstore app token** on `/api` (via `/v1/keys`, `aud == <appId>`, `exp`) — you already do
   this for the hub flow.
3. **Treat `sub` as the account/user**, not a device (on the web, `sub` is the customer account).
4. **Enforce `scope`** — see the scope format below.

## `scope` format (implemented)

The token's `scope` claim is a **space-separated list of the account's scopes that touch this app**:

- `app:<appId>` — access to the app (the door).
- `app:<appId>:<feature>` — a specific feature/tier (e.g. `…:premium`, `…:admin`). App-defined suffix.
- `app:*` — the all-access wildcard plan (grants every app).

The SPA/backend maps the **suffix** to features (e.g. `:admin` → admin panel, `:premium` → premium
functions). Absence of a token at all (the **403 `locked`**) is the "no access" case; a present token
always carries at least an app-level scope.

## App id — `gcstore.gcruiter` (confirmed)

The token's `aud` = the `{appId}` in the delivery URL = the app's id in gcstore. GCruiter pins its
expected audience via **`GCRUITER_APP_ID=gcstore.gcruiter`** (prod env), so both sides use
**`gcstore.gcruiter`**: register the app in gcstore under that id, the SPA calls
`/v1/delivery/gcstore.gcruiter`, and scope strings are `app:gcstore.gcruiter[:<feature>]`. (A
`com.gc.recruiter` default exists in code but is overridden by the env.)

## Still to decide (next slice — `/user-api` and `/admin-api`)

Today GCruiter gates `/user-api` (favorites, resume) and `/admin-api` (owner) with the **Keycloak** token
directly, not the gcstore token. The **entry gate above** (delivery `locked`/`200`) is the first
integrable slice. Making the **human paths** honor entitlement server-side is the next step, and needs a
decision: either gcstore **projects** the tier into the Keycloak token (roles/claim) so `/user-api` reads
it as it already reads `owner`, or GCruiter also consults the gcstore token/scope on those paths. Not
built yet — do not wire it until we pick.
