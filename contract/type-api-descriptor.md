# `type: api` — the app descriptor (L1) — hub renders, app backend serves

> **Status: v1 (L1) agreed** with the first api backend (GCruiter) — the amendments `result.fields`
> (per-field render) and `result.itemsPath` (paginated envelope) are folded in below; descriptor is
> public by default. Companion to the
> token contracts: [`type-api.draft.md`](type-api.draft.md) (server ↔ hub ↔ backend, the token) and
> [`type-api-local-channel.draft.md`](type-api-local-channel.draft.md) (standalone token vending). This
> one is how an **embedded** api app describes itself so the hub can render a real UI generically —
> **no bespoke UI per app**. Standalone apps bring their own UI and ignore this.

## The idea

After the hub brokers the scoped token (see `type-api.draft.md`), an **embedded** api app is rendered by
a **generic, descriptor-driven shell** in the hub. The app's backend publishes a **descriptor** that
lists its **actions** (its callable endpoints), each with an **L1** spec — title, method, path, typed
inputs, and a result render hint. The hub turns each action into a form + a result view. One shell, N
apps, zero per-app UI code. Richer widgets (L2) and full custom UIs (L3 = standalone) come later; this
is L1.

**The app owns its shape.** The descriptor is served by the app's **own backend** — not the gcstore
server (which stays a pure token broker). The app evolves its descriptor independently.

## Discovery — the endpoint the app promotes

The hub fetches the descriptor from the app's API host, authenticated with the brokered token:

```
GET  {apiBaseUrl}/.well-known/gcstore-descriptor.json
Authorization: Bearer <the scoped Ed25519 token>
```

- `{apiBaseUrl}` is the one the token delivery returned.
- **Public by default** — the descriptor exposes only the API's *shape*, no user data. The hub sends
  its `Authorization: Bearer <token>` anyway; an app that keeps the path public simply ignores it, and
  an app that prefers to gate it may (the hub always has the token). GCruiter serves it public.
- `200` → the descriptor JSON below. Any non-200 → the hub falls back to the raw request view (L0).
- The path is a fixed convention for v1. (A future `descriptor`/`descriptorUrl` in the delivery
  response may override it; not needed now.)

## Descriptor schema (L1)

```json
{
  "descriptorVersion": 1,
  "app": {
    "id": "gcstore.gcruiter",
    "title": "GCruiter",
    "summary": "Talent pipeline for the GC store."
  },
  "actions": [
    {
      "id": "list-candidates",
      "title": "List candidates",
      "method": "GET",
      "path": "/candidates",
      "inputs": [
        { "name": "q",      "label": "Search",  "type": "string", "in": "query", "required": false },
        { "name": "status", "label": "Status",  "type": "enum",   "in": "query", "options": ["open", "closed"] },
        { "name": "limit",  "label": "Limit",   "type": "number", "in": "query", "default": 20 }
      ],
      "result": {
        "render": "table",
        "itemsPath": "items",
        "fields": { "profileUrl": "link", "updatedAt": "datetime" }
      }
    },
    {
      "id": "get-candidate",
      "title": "Candidate detail",
      "method": "GET",
      "path": "/candidates/{id}",
      "inputs": [
        { "name": "id", "label": "Candidate ID", "type": "string", "in": "path", "required": true }
      ],
      "result": {
        "render": "keyValue",
        "fields": { "resumeUrl": "link", "bio": "html", "createdAt": "datetime" }
      }
    },
    {
      "id": "add-candidate",
      "title": "Add candidate",
      "method": "POST",
      "path": "/candidates",
      "inputs": [
        { "name": "name",  "label": "Name",  "type": "string", "in": "body", "required": true },
        { "name": "email", "label": "Email", "type": "string", "in": "body" },
        { "name": "remote","label": "Remote?","type": "bool",  "in": "body", "default": false }
      ],
      "result": { "render": "keyValue" }
    }
  ]
}
```

### `app`
- `id` — must match the catalog `appId` (the token's `aud`).
- `title`, `summary` — shown as the shell's header.

### `actions[]` — the available endpoints, each an L1
- `id` — stable, unique within the descriptor.
- `title` — button/tab label in the shell.
- `method` — `GET | POST | PUT | PATCH | DELETE`.
- `path` — appended to `apiBaseUrl`. May contain `{name}` segments, filled from `in: "path"` inputs.
- `enabled` — optional bool, default **true**. A simple on/off toggle: `false` hides the action from
  the hub's UI (the app chooses which endpoints to expose). Absent = on. *(Owner note: today the app
  decides via the descriptor; later this becomes operator/user-configurable in the hub, but the
  descriptor stays the default.)*
- `inputs[]` — the form the hub renders (below).
- `result.render` — how the hub renders the response (below).

### `inputs[]` — typed form fields
| field      | meaning |
| ---------- | ------- |
| `name`     | wire name (query key / path token / body key) |
| `label`    | UI label (defaults to `name`) |
| `type`     | `string` \| `number` \| `bool` \| `enum` \| `date` (L1 set; more in L2) |
| `in`       | `query` \| `path` \| `body` \| `header` (defaults to `query` for GET, `body` otherwise) |
| `required` | bool (default false) — the shell blocks submit if empty |
| `default`  | prefilled value |
| `options`  | for `enum` — the allowed values (rendered as a dropdown) |

The hub composes the request: `path` tokens from `in:path`, a query string from `in:query`, a JSON body
from `in:body`, extra `in:header` fields, always plus `Authorization: Bearer <token>`.

Render is **two levels**: `result.render` picks the *container*, and the optional `result.fields`
says how individual fields/columns render inside it. A single object commonly mixes natures (a link, an
HTML blob, dates, a JSON payload), so one hint per action isn't enough — hence per-field.

### `result.render` — the container hint
- `json` — pretty-printed JSON (the safe default; used if omitted or unknown).
- `text` — raw text.
- `keyValue` — a single object rendered as a label/value list.
- `table` — an array of objects rendered as a table (columns = union of keys).
- `link` — the whole result is a URL (plain string, or an object with a `url` field) → a clickable
  link opened in the user's **external browser** (same as the `web` app type).
- `html` — the whole result is an HTML fragment/document → rendered as formatted content in a
  **sandboxed, no-script** view (the backend is untrusted: markup shown, scripts/active content not run).

Unknown/absent container hint ⇒ `json`.

### `result.fields` — per-field render (optional, additive)
An optional map `fieldName → fieldRender` layered on `keyValue` (per field) or `table` (per column).
Field-render vocabulary: `text | number | bool | date | datetime | link | html | json | pop-up`.
- `link` → clickable, external browser; `html` → the sandboxed no-script view; `json` → code block;
  `date` / `datetime` → formatted; `text`/`number`/`bool` → plain.
- `pop-up` → shows only the first **250 characters** inline (a clickable preview); clicking it opens a
  modal (fixed **500px** wide, vertical scroll if needed) with the **full** field value and an **✕**
  close button that returns to the data view. For long text (job descriptions, notes) that would
  otherwise dominate a row.
- A field with no entry falls back to plain scalar / `json` — the same graceful-degrade rule. Apps that
  don't need it omit it entirely.

### `result.itemsPath` — paginated list envelope (optional, for `table`)
When the array is nested in an envelope — e.g. `{ "page":0, "size":50, "total":323, "count":50, "items":[…] }` —
set `itemsPath` to the key holding the array (e.g. `"items"`). Absent ⇒ the whole response *is* the
array (current behavior; bare-array endpoints need nothing). When present and the envelope carries
`page` / `size` / `total`, the hub MAY render pager controls wired to the action's `page` / `size` inputs.

L2 will add typed widgets (`cards`, `chart`, …) keyed by the same fields — a reusable hub component per
type, not per-app code — this is where "typing the templates" grows.

## What the hub guarantees (the rendering contract)

- Fetches the descriptor once per open (cached for the token's lifetime); re-fetches on token refresh.
- Renders the action list; for the selected action, a form from `inputs`; on submit, the composed
  authenticated request; then the response via `result.render`.
- Never invents endpoints — it only calls what the descriptor declares.
- A missing/invalid descriptor degrades to the raw request view (L0) — never a dead app.

## App-backend responsibilities (this half)

- Serve `GET /.well-known/gcstore-descriptor.json` (token-gated is fine), returning the schema above.
- Keep `actions` in sync with the real endpoints; verify the Bearer token on every call (per
  `type-api.draft.md`).

## Roadmap
- **L1 (this doc):** actions + typed inputs + basic result renders. Ship first; GCruiter is the pilot.
- **L2:** richer input widgets (autocomplete-from-endpoint, file) and result widgets (cards, chart),
  selected by new `type`/`render` values — reusable hub components.
- **L3:** full custom UI = a standalone app (own binary), which ignores this descriptor entirely.

## Ratified (v1, with GCruiter)
1. **Discovery path** `/.well-known/gcstore-descriptor.json` — accepted.
2. **Public** descriptor — accepted (shape only, no user data; the hub still sends the token, ignored if public).
3. **Vocabularies** — input `type`s as-is; render extended with **`result.fields`** (per-field) and
   **`result.itemsPath`** (paginated envelope). L2 grows the field/widget vocab by demand.

Note: the descriptor is **read-only** — control/trigger endpoints (collect, discovery/run) are
admin-only, on a separate platform, and never appear here.
