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
