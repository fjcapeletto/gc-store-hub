# App icons — the publisher's mark, the hub's envelope

> Normative. Part of the catalog contract (`catalog.schema.json`). A change here
> is bilateral: the server publishes what this describes; the hub renders it.

## The rule of authority

The **publisher owns the icon**. The hub must never invent or "improve" an app's
identity mark — that decision belongs to whoever publishes the app, expressed
through the catalog. The hub only owns the **envelope**: the format and size it
can render well, and the behaviour around fetching, caching, and refreshing.

This replaces the earlier arrangement where the hub carried a fixed set of
hand-drawn glyphs and picked one per app. That made the *hub* the icon authority,
which is wrong. Glyphs survive only as a graceful fallback (below).

## Resolution order (hybrid)

For each catalog app the hub resolves the icon in this order:

1. **`iconUrl`** — the publisher's own asset. Fetched and cached (below). This is
   the intended path; publishers should ship one.
2. **`icon`** — a key from the hub's on-brand glyph vocabulary. Used only when
   `iconUrl` is absent, or present but unfetchable (offline, 404, bad payload).
   The hub draws and tints these to the PCB palette; the publisher does **not**
   control their pixels — the key only *selects* among hub-drawn marks.
3. **Generic chip** — when neither is usable (no `iconUrl`, and `icon` missing or
   an unrecognized key).

The fallback chain never blocks the shelf: a slow or failed icon fetch degrades to
the glyph/chip; it must never delay or break rendering a tile.

## The envelope — what a publisher must send in `iconUrl`

The hub does its best to fit whatever arrives into the tile's chip frame, but it
renders well only within these bounds. Publishers should honour them; the hub
clamps/scales anything else rather than refusing it.

| Aspect | Requirement |
| --- | --- |
| **Transport** | `https://` URL. No credentials, no PII in the URL/query. |
| **Format** | **PNG** with alpha (canonical). JPEG and WebP accepted. **SVG is not accepted** — a vector can carry script; the hub will not execute untrusted markup. |
| **Geometry** | **Square.** Recommended **256×256**; minimum 128×128; maximum 1024×1024. Non-square is center-fit with transparent padding, but expect cropping to look off. |
| **Safe area** | Keep the meaningful mark within the centered **~90%**. The chip frame has rounded corners and inner padding; edges may be clipped. |
| **Weight** | Target **≤ 256 KB**. Larger is fetched but wastes cache and bandwidth. |
| **Background** | Transparent preferred so the mark sits on the chip. Opaque is allowed; it will show as a square inside the frame. |

**Rendering promise.** The hub scales uniformly (preserves aspect), centers, and
fits into the chip box. Publisher art is rendered **as-is** — it is **not** tinted
to the PCB palette, because it is the publisher's brand. (Only the fallback
vocabulary glyphs are hub-tinted.) Getting a mark to look right in the small chip
is a try-and-adjust exercise on the publisher's side; the hub gives best-effort
fitting, not layout guarantees.

## Caching and refresh

- The hub **caches** a fetched icon locally, keyed by its URL, when it first
  encounters the app (catalog load / install).
- **The URL is the icon's version token.** If the `iconUrl` for an app changes in
  the catalog — *even when the app `version` is unchanged* — the hub treats it as
  a new icon: it re-fetches and replaces the cached image. Same URL ⇒ served from
  cache, no re-fetch.
- This is deliberate: it lets the store refresh an app's mark for a campaign or a
  seasonal look without shipping a new app version. Point `iconUrl` at a new
  asset (or a versioned path) and every hub picks it up on its next catalog poll.
- The hub *may* additionally honour HTTP `ETag`/`Last-Modified` for freshness on
  an unchanged URL, but the **contract guarantee** is only the URL-change rule
  above — so a publisher who wants a guaranteed refresh must change the URL.

## The fallback glyph vocabulary

These are the keys the hub currently recognizes for `icon` (hub-drawn, tinted).
The set is versioned here; adding a key is a bilateral contract change and a hub
drawing — it is **not** something the hub does on a whim.

`cloud` / `weather`, `cpu` / `sensor`, `database` / `backup`, `photo` / `photos`,
`music`, `calendar`, `notes`.

Any other key (e.g. `tag`) is currently unrecognized and resolves to the generic
chip. The intended fix for such an app is to ship an `iconUrl`, not to grow the
vocabulary — the vocabulary exists for offline/asset-less degradation, not as the
primary channel.

### Note for GC Deals (`com.gc.deals`)

It currently publishes `icon: "tag"` with no `iconUrl`, so it renders as the
generic chip. To give it a real mark, the server should publish an `iconUrl`
(a price-tag PNG per the envelope above). No hub change is required for that.
