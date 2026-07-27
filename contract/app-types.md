# App types — the shelf holds more than packaged apps

> Overview + roadmap for the hub & server. Every catalog item is an "app" with a **`type`** that
> decides how it is delivered and run. This is the one-level-up map so both sides can design for
> **reuse/componentization** across types. Concrete per-type contracts link from here.

## The shared spine (identical for every type)

All types ride the **same entitlement pipeline** — build it once, reuse for all:

1. **Catalog** lists the item (visible to everyone), carrying its `type`.
2. **`/v1/authorize`** decides device access (granted / locked / access-pending) — the gate.
3. **`/v1/delivery/{appId}`** hands over what the item needs, server-re-verified — gated.
4. **Revocation** is uniform: a revoked/expired device gets `403 + offer` from delivery, for **any**
   type. This is why revocation is clean regardless of type.

**Only two things vary by type:** (a) what `/v1/delivery` returns, and (b) what the hub does with it.
Everything else (visibility, auth, gate, revoke) is shared. Design the spine once; each type is a thin
branch on top.

## The four types

| `type` | What it is (the discriminator = **who renders the content**) | delivery returns | hub does | status |
|--------|--------------------------------------------------------------|------------------|----------|--------|
| **`app`** | A packaged binary (native/sdk). | a **zip package** (+ sha-256) | download → verify → extract → **launch** | **built** (client half) |
| **`web`** | **Links** pushed to the user, opened in the **external browser**. The hub renders *no content* — it hands links off. | a gated **link-list** (items) | subscribe → poll → **systray toast** on new → **inbox** of links → open in external browser | **NEXT** (GC Deals) → [`type-web.md`](type-web.md) |
| **`feed`** | **Content** (XML/article/etc.) the **hub renders in a built-in reader** we build. | a gated **content stream** | subscribe → poll → render in the **reader** | later |
| **`api`** | A **generic, contract-driven client shell** in the hub; N "apps" = N API descriptors sharing one shell. GCStore is the **token broker**. | a **descriptor (API contract)** + a **token** | render a form from the descriptor → call the API with the token → render the response | later (highest leverage) |

The clean line: **`web` = external browser renders** (no reader, thinnest); **`feed` = our reader
renders** (build a reader); **`api` = our generic shell renders** (build a shell); **`app` = the OS
runs a binary**.

## Reuse / componentization to design for now

The thin types (`web`, `feed`, `api`) share more mechanics than they differ:

- **Subscribe / unsubscribe** — a client-side opt-in + a gated **poll** on a cadence (even in the tray).
  One component; `web` and `feed` both use it (`api` is on-demand, not polled).
- **Notifications + inbox** — new-item detection → **systray toast** + a local **inbox** list. Shared by
  `web` (links) and `feed` (content headlines); only the click target differs (external browser vs the
  reader). Build the notify/inbox once.
- **The gated poll + revocation** — one delivery/poll mechanism for all thin types.
- **Server side**: a `web` link-list, a `feed` content stream, and an `api` token+descriptor are three
  shapes over the **same gated delivery**; a shared "publish item → serve gated list" backend serves
  `web` and `feed` alike.

So building `web` well lays most of the rails for `feed`; `api` reuses the auth/gate/token spine but
brings its own descriptor-driven shell.

## Roadmap (simplest → most complex)

1. **`web`** (GC Deals) — links + push + inbox, external browser. Proves entitlement + delivery +
   **revocation** + notifications at the lowest cost. See [`type-web.md`](type-web.md).
2. **`feed`** — reuse the subscribe/poll/notify spine, add the in-hub reader.
3. **`api`** — reuse auth/gate, add the token-broker + the generic descriptor-driven shell (one shell → N apps).
4. **`app`** — the packaged pipeline (client half already built); proves the zip machinery with app #1.

## Catalog field

Each catalog item declares its type: **`type: "app" | "web" | "feed" | "api"`**, default **`app`**
(back-compatible — existing items stay packaged apps). Added to `catalog.schema.json`.
