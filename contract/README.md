# GC Store — Boundary Contract

This folder is the **single source of truth for the wire boundary** between the two
repos of the GC Store system:

- **`gc-store-hub` (this repo, public)** — the Windows hub client on the refurbished
  laptops. Untrusted; runs on machines we don't control.
- **`gc-store-server` (private)** — the entitlement / licensing / app-delivery backend.
  The source of truth for who may install what.

The split is a **trust boundary**. Everything about signing keys, entitlement
enforcement, catalog administration, and ERP coupling lives on the server and must
never appear in this public repo. What lives *here* is only the shape both sides
agree to speak across the wire — because the client is public, that shape is
inherently observable and therefore safe to publish.

## Why this folder exists

Claude Code memory is per-repo and does not sync. The only state both the hub session
and the backend session can reliably see is what's in git. So the contract is anchored
as a versioned file here rather than in either side's memory. **Contract changes are
bilateral and land in this folder first**; the backend implements against it, the hub
consumes it.

## What is committed vs. what is not

Two kinds of files live here, and only one kind is committed:

- **Normative interface (committed & persisted)** — the ratified wire shapes and usage
  mechanics, from the public hub side, exposing no server logic. Plain filenames
  (`README.md`, `catalog.schema.json`, `device-authorization.md`, …). Contracts must be
  persisted, so these belong to the repo.
- **Negotiation vaivão (NEVER committed)** — the drafts / reviews / responses / ratifications /
  requests / handoffs the two agent sessions exchange to reach agreement. Volatile: scoped to
  the round, then they die. They are **git-ignored** by role suffix
  (`*.draft.md`, `*.review.md`, `*.response.md`, `*.ratify.md`, `*.request.md`, `*.update.md`,
  `*-handoff.md`, `*.hub-*.md`, `*.backend-*.md`) so they cannot be committed by accident.

When a round closes, the **agreed content is folded into the normative doc** (which gets
committed) and the negotiation drafts are discarded.

## The visibility model

The catalog is **visible to every device**. What a purchased/entitled device gets is not
a *different shelf* — it's the *right to install*. An unentitled machine sees the full
catalog with every app **locked** (the client renders them opaque) and an **offer** in
place of the install action. This is deliberate: the shelf itself is the sales hook — you
can want the store, and buy access to it, even on a machine that didn't ship with it.

**The lock is UX only.** The binding enforcement is the delivery endpoint refusing to
serve the package/license to a non-granted device (see *Enforcement* below). A spoofed
client that flips a `locked` flag to `installable` still gets nothing to install.

## Catalog request (device-scoped)

The hub asks for the catalog with a **`POST`** carrying a device claim, so the response
can be scoped to that device. (Today the hub still does an anonymous `GET` of a static
file via `Catalog:Url` / `GCSTORE_Catalog__Url`; moving to the claim `POST` is the first
backend integration step.)

- **Privacy rule:** never put the license key, fingerprint, or any PII in the URL/query.
  The claim travels in the request body over TLS.
- **Claim body** (fields the hub's `Identity/` collector can present):

```json
{
  "license": "<device license key — the identity anchor>",
  "fingerprint": { "...": "advisory/soft signals: MAC, WMI attributes" },
  "attestation": { "tpmEk": "<optional TPM endorsement key, when available>" }
}
```

The `license` is the anchor; `fingerprint` is advisory; `attestation.tpmEk` is an optional
hardware-attestation tier. The raw license may later be replaced by a signed device token.

## Catalog response (manifest)

A JSON document matching [`catalog.schema.json`](catalog.schema.json). camelCase
properties; enums kebab-case.

- **`access`** — the device's store-access state: `granted` or `locked` (with an optional
  `reason` and, when locked, an `offer`). This is the device-wide default.
- **`apps`** — always the full catalog. Each app **may** carry an `install` block to
  override installability for that app alone; absent means it inherits `access.state`.
- **Resolution rule:** an app is installable when `app.install.state == "installable"`, or
  when `install` is absent and `access.state == "granted"`. Otherwise it is locked and the
  client shows the offer (`app.install.offer` if present, else `access.offer`).

```json
{
  "catalogVersion": 3,
  "access": {
    "state": "locked",
    "reason": "no-store-access",
    "offer": {
      "headline": "Unlock the Gabriel Capeletto Store",
      "actionUrl": "https://.../unlock/{opaque-token}"
    }
  },
  "apps": [
    {
      "id": "com.gc.weather",
      "name": "Weather",
      "version": "1.3.0",
      "summary": "Local forecast at a glance.",
      "identityMode": "device-only",
      "icon": "cloud"
    }
  ]
}
```

A `granted` device gets the same `apps` with `access.state: "granted"` and no `offer`;
the client renders the apps installable (still subject to each app's `identityMode`).

## Enforcement (the real gate)

`access`/`install` states drive the UI. The authoritative gate is the **delivery /
entitlement endpoint**: when the client requests an app package or a license, the server
**re-verifies the device's entitlement server-side** and refuses (e.g. `402`/`403` plus an
offer) for a non-granted device. Installability signaled in the catalog is a hint for
rendering, never a decision the client is trusted to make.

> Server-side is the source of truth. The client runs on untrusted machines and never
> decides entitlements on its own. Do not add client-side gating a spoofed client could
> skip.

## Device authorization & entitlement

The `access`/`install` states above are **render hints**. The authoritative machinery —
the device-authorization handshake, the signed entitlement **lease**, package **delivery**
with per-device key envelopes, protection tiers, and key rotation — is specified normatively
in **[`device-authorization.md`](device-authorization.md)** (shapes in
[`device-authorization.schema.json`](device-authorization.schema.json)).

Two consequences reach into this catalog contract:

- Each app declares a **`protection`** tier (`sdk` | `native`, default `native`) alongside
  `identityMode`. It sets how strong the runtime gate is and whether revocation can take
  effect mid-session (`sdk`) or only at next launch (`native`).
- The hub is the **mandatory runtime gate**: app payloads are delivered encrypted and only
  the hub, holding a valid lease, decrypts them to launch. The `access`/`install` hints must
  stay consistent with the lease — never render `installable` what the lease says is
  `revoked`.
