# GC Store — Device Authorization & Entitlement

> **Status: normative.** Bilaterally ratified between `gc-store-server` (backend) and
> `gc-store-hub` (client). Supersedes the `device-authorization.*.draft/review/response/
> ratify` vaivém files, which may be deleted.
>
> **Wire shape only.** The client is public, so these shapes are inherently observable and
> safe to publish. How the server *mints* signatures, *wraps/seals* keys, or couples to the
> ERP is private server logic and never appears here.

This extends the [catalog contract](README.md). The catalog is the **shelf + render
hints**; this document is the **cryptographic teeth** — what actually authorizes running an
installed app and hands over a (encrypted) package.

All bodies are JSON, `camelCase` properties, `kebab-case` enums, timestamps RFC 3339 UTC —
except the **lease**, which is a JWS compact string (§3).

---

## 1. Enforcement model

The hub is the **mandatory runtime gate** — without it an app does not start:

- **Launch gate (Kindle-style).** App payloads are delivered **encrypted and inert**. The
  hub verifies the lease and decrypts to launch. No hub / no valid lease ⇒ no launch.
- **Continued-use check (Apple-Music-style).** Paid entitlements expire; the running app
  keeps re-checking that its entitlement is still valid (§6).

**Uniform gate, per-app policy.** Every app passes the gate, but lease policy differs so
free apps are not hostage to connectivity:

- **Free / device-only → perpetual (non-expiring) entitlement.** A permanently-offline
  device still launches it: the hub verifies offline and decrypts. No phone-home.
- **Paid / subscription → expiring entitlement + renewal.** Offline grace until `notAfter`,
  then the gate refuses (revocation leash).

### 1.1 Protection tiers (declared per app)

Encryption on an untrusted machine is only as strong as what holds the plaintext at run
time. Two tiers, declared per app (see `protection` in the catalog schema):

| Tier     | Runtime                         | Protection | Revocation                         |
|----------|---------------------------------|------------|------------------------------------|
| `sdk`    | Runs in the hub runtime / SDK; payload stays in a controlled process | **Strong** — encryption is real | Launch gate **and** mid-session (§6) |
| `native` | OS-loaded native process (e.g. Sensor Bridge, Backup) | **Deterrence** — plaintext is capturable once loaded | Launch gate **only** — takes effect next launch |

- `native` mid-session revocation has **no teeth**: once the OS has loaded the process the
  hub cannot stop it (a best-effort process-kill by the launcher is not enforcement, and is
  nil against an extracted copy). Do not imply native paid apps can be cut off in real time.
- **Default is `native`** (fail-safe: never over-claim protection for an undeclared app).
- Prefer `sdk` + `tpm-seal` for anything whose revenue actually needs protecting.

**Honest ceiling.** You cannot make bytes uncopyable on hardware you do not control. The
goal is inert copies and cost-above-payoff. The irreducible ceiling is the decrypt moment /
a patched hub — universal to all local DRM. `native` + `fingerprint` is the weakest corner,
`sdk` + `tpm-seal` the strongest.

---

## 2. `POST /v1/authorize` — device authorization & renewal

The hub presents its device claim plus its capabilities; the server returns a signed lease.
This is also the **renewal** endpoint (called proactively before expiry — §5).

**Request:**

```json
{
  "claim": {
    "deviceId": "<hub-generated, opaque, persisted — see device-identity-provisioning.md>",
    "license": "<device license key — the identity anchor>",
    "fingerprint": { "...": "advisory/soft signals: MAC, WMI attributes" },
    "attestation": { "tpmEk": "<optional TPM endorsement key, when available>" }
  },
  "capabilities": {
    "schemes": ["tpm-seal-v1", "fingerprint-wrap-v1"],
    "canSealTpm": true,
    "hasTpmEk": true,
    "trustedKeyIds": ["k3", "k2"]
  }
}
```

- **`deviceId`** — generated on the device, not by the server (see
  `device-identity-provisioning.md`). Paired with `license` in the DB `device` row. On the first
  successful authorize the server **binds the claim's `fingerprint`** to this pair (activation);
  later a materially different fingerprint ⇒ reject / flag (§8).
- **`license`** — **optional.** A license-less device with a pending request still authorizes, to
  receive `403 access-pending`. Omitted from the body when the device holds no license yet.
- **Privacy rule:** never in the URL/query — the body travels over TLS.
- **`capabilities.schemes`** — key-envelope schemes the client can unwrap (§4).
- **`canSealTpm`** — the hub can seal/unseal a key to this device's TPM (strong delivery
  binding). Distinct from **`hasTpmEk`** — the hub can read the EK public key (identity
  attestation). EK presence does **not** imply seal capability; refurb TPM is heterogeneous.
- **`trustedKeyIds`** — the lease-verification `keyId`s embedded in this device's installed
  hub release. The server MUST sign with a `keyId` in this set (§7).

**Responses:**

- `200` — a signed lease (§3).
- `402` / `403` — no store access; body carries an `offer` (catalog `offer` shape) + a `reason`,
  no lease. `reason` ∈ `unrecognized-device` | `no-store-access` | **`access-pending`** (a device
  that submitted an access request still awaiting the owner's approval).
- `409` — the server has retired every `keyId` the device still trusts; the hub must update
  online before it can verify any lease (fail-closed).

### 2.1 `POST /v1/access-request` — request store access (a-posteriori path)

For a machine with no license: the hub submits the user's details + its generated `device_id`.
The server stores it as `requested` for manual approval; on approval the owner emails the
`license_key` (see `device-identity-provisioning.md`).

**Request:** `{ "deviceId": "...", "name": "...", "email": "...", "phone": "<optional>", "fingerprint": { ... } }`

**Responses:** `202 { "status": "requested" }` (accepted / already pending). The hub then shows
an "access requested" state until a license is entered and authorize returns a lease.

---

## 3. The entitlement lease (JWS)

The lease is a **JWS Compact Serialization** (RFC 7515) signed with **EdDSA / Ed25519**
(RFC 8037): `base64url(header).base64url(payload).base64url(signature)`. The hub verifies
the signature over the exact `header.payload` ASCII bytes **offline, without
re-serializing**, then decodes the payload to read the claims. No JSON canonicalization.

**Header:** `{ "alg": "EdDSA", "kid": "<keyId ∈ trustedKeyIds>" }`

**Payload claims:**

```json
{
  "sub": "<deviceId — opaque, server-assigned; NOT the license key>",
  "iat": 1753294800,
  "exp": 1753899600,
  "tier": "tpm",
  "entitlements": [
    { "appId": "com.gc.weather", "state": "granted" },
    { "appId": "com.gc.notes",   "state": "granted", "notAfter": 1753899600 },
    { "appId": "com.gc.music",   "state": "revoked" }
  ]
}
```

- **`sub`** — the device id; the hub MUST check it equals the identity it resolves locally
  (anti-replay; see §8).
- **`iat`** — issued-at; the hub's "last trusted time" anchor (§5).
- **`exp`** — the **renewal horizon** = earliest paid `notAfter`. Omitted when every
  entitlement is perpetual. Reaching `exp` means "refresh needed", not "everything dies".
- **`tier`** — the binding tier actually granted: `tpm` | `fingerprint`.
- **`entitlements[].state`** — `granted` | `revoked`. The hub launches an app only when its
  entitlement is `granted` and (if `notAfter` present) still within validity.
- **`entitlements[].notAfter`** — **per-app** hard cutoff. **Absent = perpetual** (free /
  device-only). Present = expiring (paid). A permanently-offline device keeps launching its
  perpetual entitlements regardless of the lease-level `exp`.

---

## 4. `POST /v1/delivery/{appId}` — package delivery

Requested to install or update an app. The server **re-verifies entitlement server-side**
(never trusts the lease alone) and, if granted, returns a pointer to the **ciphertext**
package plus a **per-device key envelope**.

**Request:** same `{ claim, capabilities }` as §2.

**Response `200`:**

```json
{
  "appId": "com.gc.weather",
  "version": "1.3.0",
  "package": {
    "url": "https://.../pkg/{opaque}?exp=...&sig=...",
    "expiresAt": "2026-07-23T18:05:00Z",
    "sizeBytes": 12345678,
    "hash": { "alg": "sha-256", "value": "<hex>" }
  },
  "keyEnvelope": {
    "scheme": "tpm-seal-v1",
    "binding": "tpm",
    "wrappedKey": "<base64 — content key wrapped so only this device can unseal>"
  }
}
```

- **`package.url`** — a **short-lived, single-use signed URL** to **ciphertext**. Storage is
  private and non-listable; the URL expires in minutes.
- **`package.hash`** — integrity check for the downloaded ciphertext.
- **`keyEnvelope.scheme`** — chosen from `capabilities.schemes ∩ server`. The server **fails
  closed** (`402`/`403` with `offer`, no package) when there is no overlap or the device is
  not granted.
- **Decoupled lifetimes:** `package.url` = minutes; `wrappedKey` = device-bound, not
  time-bound.
- **`402` / `403`** for an ungranted device — with an `offer`, never a package.

---

## 5. Renewal & clock skew

- **Proactive renewal.** The hub re-`POST`s `/v1/authorize` at **~50–75 % of the paid
  window (with jitter)**, not at `exp`, so a paid app never hard-stops on a transient blip
  near the boundary. `notAfter` remains the hard cutoff; proactive renewal keeps a connected
  device from reaching it, and jitter spreads server load.
- **Clock skew.** Refurb laptops routinely boot with a wrong clock. The hub:
  - tolerates **±5 min** on `iat` / `exp` / `notAfter`;
  - anchors to the lease `iat` as "last trusted time" plus a monotonic timer;
  - treats gross clock anomalies as **"needs online renewal"**, not a hard fail.
  - For a permanently-offline device, expiry is unreliable anyway — the gates that matter
    (install, paid continued-use) lean on delivery-time re-check and renewal.

---

## 6. Continued-use heartbeat (SDK tier — app↔hub, not a server endpoint)

> **Not to be confused with the hub's liveness / shelf-sync signal (the green/red LED).**
> That is a *hub → server* connectivity + catalog-freshness heartbeat (periodic + a manual
> Sync button) that keeps the shelf, installs, and upgrades current — a separate concern
> that belongs to the catalog side. The heartbeat below is *app → hub*, local, and about
> **entitlement enforcement** only. The two may share one phone-home but are distinct.

The heartbeat is an **SDK-local call from the running app to the hub**, not a per-app server
request. The app periodically asks the hub "is `appId` still entitled?"; the hub answers from
the lease it holds (refreshing via `/v1/authorize` when needed). This keeps the server free
of per-heartbeat load.

- Enforceable **only at `sdk` tier** — the SDK holds the process and can stop it mid-session.
- **`native` tier has no mid-session enforcement** — revocation applies at the next launch
  gate only.

---

## 7. Trust anchor & key rotation

- The hub verifies the lease against a set of `keyId → publicKey` entries **embedded in its
  signed release** (the verification key is public; only the private signing key is secret).
- Rotation ships in a hub update. It is therefore **bounded by update propagation** — the
  signing key cannot rotate faster than releases reach devices, and a permanently-offline
  device never gets a new key.
- **Overlapping validity, client-negotiated.** The client advertises `trustedKeyIds` (§2);
  the server signs with a `keyId` in that set. A freshly-updated device offers new+old and
  gets the new key; an old/offline device offers only old and keeps the still-valid old key
  until it is retired. When the server has retired **every** key a device still trusts, it
  responds `409` (§2) and the device must update online — fail-closed.
- A compromised signing key is a real incident for un-updated/offline devices; it lives in
  the secret manager and hardware-backed server-side signing is worth considering. *(Private
  detail — stated as a constraint, not topology.)*

---

## 8. Anti-replay & UX/enforcement consistency

- **Anti-replay.** The hub checks lease `sub` == locally-resolved `deviceId`. At `tpm-seal`
  tier, delivery replay is intrinsically dead (the wrapped key won't unseal on device B); at
  `fingerprint` tier this check is the soft primary defence. It stops **casual copying, not a
  patched hub** — the same untrusted-client ceiling as everything else.
- **UX consistency.** The catalog's `access` / `install` states are UI hints only; the hub
  MUST NOT render an app `installable` when the lease says `revoked` (or the entitlement is
  past `notAfter`). Enforcement is the lease + delivery re-check, never the catalog states.

---

## 9. Relationship to the catalog contract

| Concern           | Catalog (`README.md`)                    | This document                                   |
|-------------------|------------------------------------------|-------------------------------------------------|
| The shelf         | `apps` — full catalog, always visible    | —                                               |
| Render hints      | `access` / `install` states              | —                                               |
| App format        | `protection: sdk \| native`, `identityMode` | drives gate strength & revocation (§1.1, §6)  |
| Right to **run**  | (hint only)                              | signed **lease** (§3), enforced at launch       |
| Right to **install** | (hint only)                           | **delivery** re-verifies + serves ciphertext (§4) |

Server-side is the source of truth; the client never decides entitlement on its own.
