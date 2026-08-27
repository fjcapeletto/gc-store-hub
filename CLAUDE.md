# CLAUDE.md

Guidance for Claude Code (and human contributors) working in this repository.

> **This file is public.** It must never contain secrets, server IPs, database
> credentials, deploy topology, private-backend internals, or anything tied to
> the GabrielCapeletto ERP. Keep it to what a public reader may safely see.

## What this repo is

The **public Windows hub client** for the Gabriel Capeletto Store — the software
hub pre-installed on the refurbished laptops sold by GabrielCapeletto LLC. It is
one side of a two-repo system:

- **This repo (public):** the thin per-OS hub client.
- **A separate private repo:** the entitlement/store backend.

The split is a **trust boundary**, not just organization. Anything involving
signing keys, entitlement enforcement, catalog administration, or ERP coupling
belongs in the private backend and must not appear here.

## Non-negotiables

- **No secrets, ever.** History is permanent on a public repo. Config comes from
  `dotnet user-secrets` (dev) and env vars / a secret manager (prod). Committed
  files carry placeholders only (`.env.example`, `appsettings.example.json`).
- **The license-signing private key never touches any repo.** It lives only in
  the server's secret manager.
- **Server-side is the source of truth.** The client runs on untrusted machines;
  it never decides entitlements on its own. Do not add client-side gating that
  a spoofed client could simply skip.
- **English** for all code, comments, docs, and identifiers in this repo.

## Product shape (context, not yet built)

- The hub is an **app store**, not a single app: versioned apps, user-controlled
  updates, uninstall/reinstall from the hub.
- Current stage is **empty-hub-first**: build the empty store, prove the
  distribution pipeline before any real app exists.
- The store vitrine is **visible to every device**; entitlement gates the *right to
  install*, not visibility. An unentitled machine sees the full catalog with apps
  **locked** and an **offer** to buy store access (the sales hook). The client-side
  lock is UX only — the server withholds the package/license. See `contract/`.
- Apps declare their identity needs: device-only, account-required, or hybrid.

## Tech

- **C# / .NET, Windows-first.**
- **Desktop UI framework is undecided** (WPF vs WinUI 3 vs Avalonia). Do not
  scaffold a UI project or otherwise pin this choice until it is made.

## Working agreements

> Full conduct & process rules live in [`WORK-AGREEMENTS.md`](WORK-AGREEMENTS.md) — the
> canonical, portable copy that travels with the repo. Read it alongside this file. The
> essentials:

- **Git is owner-controlled.** Do not stage, commit, push, or branch unless
  asked. Leave the tree commit-ready and hand off. A commit and a push are
  separate approvals; never push without a fresh green light for that push.
- **`docs/` and `BACKLOG.md` are versioned with the code** — they are committed
  normally, not kept local. Keep them sanitized like everything else in this
  public repo (no secrets, backend internals, or deploy topology).
- Keep build output quiet; surface logs only on failure.
- Run one topic at a time; don't fan out parallel decisions.
