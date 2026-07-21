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
- The store vitrine is **device-authorized** at the catalog API layer — an
  unrecognized machine sees empty shelves.
- Apps declare their identity needs: device-only, account-required, or hybrid.

## Tech

- **C# / .NET, Windows-first.**
- **Desktop UI framework is undecided** (WPF vs WinUI 3 vs Avalonia). Do not
  scaffold a UI project or otherwise pin this choice until it is made.

## Working agreements

- **Git is owner-controlled.** Do not stage, commit, push, or branch unless
  asked. Leave the tree commit-ready and hand off.
- Keep build output quiet; surface logs only on failure.
- Run one topic at a time; don't fan out parallel decisions.
