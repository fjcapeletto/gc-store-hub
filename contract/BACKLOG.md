# GC Store — shared backlog (cross-session)

Deferred items both sides pick up later. Not now.

## Driver / prerequisite artifact class (free, ungated) + app `requires`

**Status: DEFERRED** — owner can't test it yet (no real driver artifact). Full design is in
[`delivery-lifecycle.hub-response.md`](delivery-lifecycle.hub-response.md) §7.

- Two artifact classes: **gated app** vs **FREE ungated driver/prerequisite** (customer owns
  the hardware → has the right to its drivers, no entitlement check).
- Apps declare a **`requires`** list; the hub resolves prerequisites first (fetch+install the
  free driver, then the gated app). Elevation (one-time UAC) is scoped to the driver step only.
- **Backend TODO:** `app_package.artifact_type` already accommodates it; still to add — a
  `requires` relation, a `requires` field in the catalog wire shape (bilateral contract
  change), an **ungated** delivery path for prerequisites, and `publish` handling prereqs.
- **Hub TODO:** prerequisite resolution + scoped one-time UAC for the driver install.
- **Blocked on:** a real driver package to test against (the serial/RS-232 hardware line,
  "coming soon").
- **Pick up after** the first gated-app E2E (install → update → uninstall → revoke) is proven.

## Hub candidates — to be confirmed

> Pulled from working notes as candidates for the roadmap. **Not yet triaged into committed
> scope** — the owner confirms which advance and when. Listed here so nothing is lost.

- **User-governed notification controls (`web` apps).** Per-app mute, digest mode (batch several
  pushes into one summary), a frequency cap, and quiet hours — the client-side half of
  notification discipline. Build alongside the web subscribe/poll/toast/inbox.
- **Visible self-update experience.** A status cue in the header ("downloading… / update ready —
  restart now") plus a manual "Check for updates" control and apply-and-restart. (Takes effect
  from the next installed version onward.)
- **Installer code-signing.** An Authenticode certificate held as a CI secret and passed to the
  packaging step, to build SmartScreen reputation for frictionless fleet-wide distribution.
- **Returning-visit banner copy.** Phrase the count as "available to install" rather than a
  historical tally, so it reads correctly as items are installed.
- **Conditional catalog polling.** ETag / `If-None-Match` → `304 Not Modified`, so idle polls
  cost almost nothing.
- **Offline lease grace.** Let free / perpetual apps keep working while the server is
  unreachable; define the grace window for paid subscriptions before a phone-home is required.
- **Server-side catalog authorization gate.** Promote the catalog fetch to the real device-auth
  gate (bilateral with the backend).
- **Real install / update execution.** Advance the Install/Update action from state-tracking to
  real package delivery + install (bilateral; pairs with the delivery lifecycle above).
- **Customer-initiated re-endorsement.** An in-app affordance to request a device re-bind when
  identity needs review (e.g. after a legitimate firmware update or a warranty board swap);
  needs the backend handshake.
- **Look & feel pass.** A dedicated visual/UX pass (cards & states, copy, the Settings / "this
  device" page, empty / error / loading, branding) and the "circuit board" design-language next
  steps: per-app vector glyphs, subtle animation, colour evolution.
- **Real application icon.** Replace the default framework icon with the GC mark.
- **OS-reinstall binding policy.** Decide strict vs tolerant re-bind — likely segmented (free
  apps tolerant, paid strict).
- **Recovery-key delivery.** Typed by the customer vs baked into an owner-provided recovery
  image / USB.
- **Warranty transfer flow.** Re-issue path when a mainboard is replaced under warranty
  (fingerprint drift → owner-approved re-bind).
- **AOT build option.** If the hub is ever AOT-compiled for a smaller OEM footprint, switch JSON
  handling to source-generation.
- **Hardware / port / driver apps (`native` tier).** The hub half of the driver-prerequisite
  item above — serial RS-232 sensing/control, free ungated driver step + gated app.
