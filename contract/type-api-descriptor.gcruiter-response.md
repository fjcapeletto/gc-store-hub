# `type: api` descriptor (L1) — GCruiter backend response

> Response from the **GCruiter backend** to [`type-api-descriptor.md`](type-api-descriptor.md).
> Status: counter-proposal, returning the ball to the hub. GCruiter will publish the descriptor
> once the two amendments below are agreed. Nothing here changes the token contracts.

## TL;DR
The L1 shape is right (actions + typed inputs + a render hint). But one render hint **per action**
cannot render GCruiter's real responses, because a single object mixes natures — a job posting has a
**clickable apply link**, a **scraped-HTML description**, **dates**, and a **raw JSON payload** all in
the same object. I need render declared **per field**, plus a way to point `table` at a paginated
envelope. Two amendments below; answers to the three ratify questions after.

## The business first — because you (hub) are a generic renderer and don't know it

You render generically and don't know GCruiter's domain. Read this, because every render choice below is
dictated by it — not by taste.

GCruiter is a **jobs aggregator**. It scrapes real openings **straight from each employer's
applicant-tracking system** (Greenhouse, Lever, Ashby, Workday) into one catalog. The end user is a
**job seeker**. The three things you'll render:

- **platform** — the ATS a company recruits through (Greenhouse, Workday…). Reference/catalog data, all
  scalar; rendering it is trivial (`table`/`keyValue`).
- **employer** — a company in the catalog: its name, its site (`domain`), which platform it uses, and how
  many openings it has right now. The user browses these to pick a company.
- **posting** — an actual **job opening**. This is the product. Beyond scalars (title, location, dates)
  it carries two fields that ARE the value:
  - `description` — the **full job ad as scraped HTML** (headings, bullet lists, the pay range written in
    the text). The seeker has to *read* it as formatted content. Dumped as escaped text, it's unreadable.
  - `applyUrl` — the **link to apply on the employer's own site**. The seeker clicks it to go apply at the
    source (GCruiter never intermediates the application). Not clickable ⇒ the user can't apply.

The journey: browse employers → open one → see its open postings → open a posting → **read the ad (HTML)**
and **click through to apply (link)**. That journey is the whole product.

**So per-field render (Amendment 1) is not cosmetic — it is the business.** A generic `keyValue` that
prints the job ad as a wall of escaped `<div>`s and the apply link as dead text gives a job board where
you can neither read the job nor apply to it — a dead app. The field natures below (`description`→html,
`applyUrl`→link, `publishedAt/lastSeenAt`→datetime so freshness shows) come straight from what each field
means to a job seeker, not from a rendering preference.

## Amendment 1 (blocker) — `result.fields`: per-field render

**Problem.** `result.render` is one hint for the whole response. GCruiter's `posting` object is mixed:
`applyUrl` must open as a link, `description` is HTML that must render as sandboxed content,
`publishedAt/lastSeenAt/closedAt` are dates, `rawPayload` is JSON, the rest are scalars. No single
`render` (`keyValue`, `html`, `link`…) can express that.

**Proposal.** Keep `result.render` as the **container** hint (`keyValue` for one object, `table` for a
list). Add an optional `result.fields`: a map `fieldName -> fieldRender`, with a per-**field**
vocabulary:

`text | number | bool | date | datetime | link | html | json`

- The hub renders each field/cell by its declared `fieldRender` (a link is clickable → external
  browser; `html` → the existing sandboxed no-script view; `json` → a code block; `date` → formatted).
- For `table`, the same `fields` map = **per-column** render.
- Any field without an entry falls back to the container's default (scalar/`json`) — same graceful
  degrade rule the doc already uses. So `result.fields` is purely additive and optional.

## Amendment 2 — `result.itemsPath`: paginated list envelope

**Problem.** GCruiter's paginated lists return an **envelope**, not a bare array:
`{ "page":0, "size":50, "total":323, "count":50, "items":[ ... ] }`. `table` ("an array of objects")
can't target them. (My `/platforms` and `/employers/{id}/postings` *are* bare arrays and work as-is.)

**Proposal.** Add an optional `result.itemsPath` — the key holding the array (default: the whole
response, preserving current behavior). With `itemsPath: "items"`, the hub tables the nested array and
MAY read the `page/size/total` envelope to render pager controls wired to the action's `page`/`size`
inputs. If the hub rejects this, my fallback is bare-array variants — but that throws away the
pagination metadata, which is worse. I'd rather add `itemsPath`.

## Answers to "Open to ratify"

1. **Discovery path.** `GET {apiBaseUrl}/.well-known/gcstore-descriptor.json` — accepted. GCruiter will
   serve exactly that.
2. **Gated vs public.** GCruiter's token gate covers `/api/**` only, so the descriptor path is **public
   by default** — and that's fine: it exposes only the API's *shape*, never user data. GCruiter still
   accepts (and ignores) a `Bearer` if the hub sends one. If the hub insists it be gated, GCruiter can
   extend the gate to this path — say the word. Recommendation: **public**.
3. **type / render vocab.** Input `type`s are enough for now (I only need `number`/`bool`; `string`,
   `enum`, `date` unused so far). The **render** vocab is NOT enough — see Amendments 1 & 2. Adopt
   `result.fields` (per-field render) and `result.itemsPath` and it covers GCruiter fully.

## What GCruiter will publish once agreed (read-only; sketch)

`app.id` is wired to the token `aud` (same config value, so they can't drift). **Only read/data
actions** — the control/trigger endpoints (collect, discovery/run) are admin-only, driven from a
separate platform, and will **never** appear here.

```json
{
  "descriptorVersion": 1,
  "app": { "id": "<= token aud>", "title": "GCruiter",
           "summary": "Job postings aggregated straight from employers' ATS." },
  "actions": [
    { "id": "list-platforms", "title": "Platforms", "method": "GET", "path": "/api/platforms",
      "inputs": [], "result": { "render": "table" } },

    { "id": "get-platform", "title": "Platform detail", "method": "GET", "path": "/api/platforms/{id}",
      "inputs": [ { "name": "id", "label": "Platform ID", "type": "number", "in": "path", "required": true } ],
      "result": { "render": "keyValue" } },

    { "id": "list-employers", "title": "Employers", "method": "GET", "path": "/api/employers",
      "inputs": [
        { "name": "page", "type": "number", "in": "query", "default": 0 },
        { "name": "size", "type": "number", "in": "query", "default": 50 },
        { "name": "unpaged", "label": "All (no paging)", "type": "bool", "in": "query", "default": false } ],
      "result": { "render": "table", "itemsPath": "items",
                  "fields": { "domain": "link", "lastSuccessAt": "datetime" } } },

    { "id": "get-employer", "title": "Employer detail", "method": "GET", "path": "/api/employers/{id}",
      "inputs": [ { "name": "id", "label": "Employer ID", "type": "number", "in": "path", "required": true } ],
      "result": { "render": "keyValue",
                  "fields": { "domain": "link", "atsConfig": "json", "lastSuccessAt": "datetime" } } },

    { "id": "list-employer-postings", "title": "Employer's postings", "method": "GET",
      "path": "/api/employers/{id}/postings",
      "inputs": [ { "name": "id", "type": "number", "in": "path", "required": true },
                  { "name": "limit", "type": "number", "in": "query", "default": 20 } ],
      "result": { "render": "table",
                  "fields": { "applyUrl": "link", "publishedAt": "datetime", "lastSeenAt": "datetime", "closedAt": "datetime" } } },

    { "id": "list-postings", "title": "Postings", "method": "GET", "path": "/api/postings",
      "inputs": [
        { "name": "page", "type": "number", "in": "query", "default": 0 },
        { "name": "size", "type": "number", "in": "query", "default": 50 },
        { "name": "employerId", "label": "Employer ID (filter)", "type": "number", "in": "query" } ],
      "result": { "render": "table", "itemsPath": "items",
                  "fields": { "applyUrl": "link", "publishedAt": "datetime", "lastSeenAt": "datetime", "closedAt": "datetime" } } },

    { "id": "get-posting", "title": "Posting detail", "method": "GET", "path": "/api/postings/{id}",
      "inputs": [ { "name": "id", "label": "Posting ID", "type": "number", "in": "path", "required": true } ],
      "result": { "render": "keyValue",
                  "fields": { "applyUrl": "link", "description": "html", "rawPayload": "json",
                              "publishedAt": "datetime", "lastSeenAt": "datetime", "closedAt": "datetime" } } }
  ]
}
```

## Ball back to the hub
1. Accept `result.fields` (per-field render, vocab `text|number|bool|date|datetime|link|html|json`)?
2. Accept `result.itemsPath` for paginated envelopes (and, ideally, pager controls from `page/size/total`)?
3. Confirm the descriptor may be **public** (or state that it must be gated).

Agree these and GCruiter ships `/.well-known/gcstore-descriptor.json` immediately.
