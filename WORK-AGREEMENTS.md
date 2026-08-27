# Working agreements

> **Purpose.** The durable conduct & process rules for anyone contributing to this
> repository — human contributors and AI coding assistants alike. This file is the
> **canonical, portable** copy: it travels with the repo, so it applies on any machine and
> in any session, regardless of local tooling or per-machine memory. `CLAUDE.md` points here;
> read both. When a rule here and a casual instruction conflict, confirm before proceeding.
>
> **This file is public.** Keep it free of secrets, internal hostnames/IPs, deploy topology,
> and backend/ERP internals — same rule as the rest of the repo.

## Git & history

- **Git is owner-controlled.** Do not stage, commit, push, or branch unless explicitly asked.
  Prepare changes, leave the working tree commit-ready, and hand off for review.
- **A commit and a push are separate approvals.** Never push without either (a) the change
  tested/validated, or (b) explicit fresh authorization for *that* push. Approval of one batch
  never carries to the next — each push needs its own green light.
- **No AI self-attribution.** Commit messages, PR bodies, and any generated artifact contain
  only the substance of the change — never a `Co-Authored-By`, "Generated with…", or similar
  trailer. The work product is the owner's.

## Secrets & the public boundary

- **No secrets, ever** — history on a public repo is permanent. Real config comes from
  `dotnet user-secrets` (dev) and env vars / a secret manager (prod); committed files carry
  placeholders only.
- **Never paste credentials in chat.** Use placeholders and published dev fixtures; the owner
  fills real values himself. Do not request a real secret in conversation.
- **The license-signing private key never touches any repo.** It lives only in the server's
  secret manager.

## Contract governance (trust boundary)

- This repo is the **public, untrusted client**; the entitlement/store backend is a separate
  private repo. The split is a **trust boundary**, not just organization.
- The `contract/` folder is the **single source of truth per boundary**. Wire-shape or
  handshake changes land as a ratified, plain-named contract here first, then the backend
  builds to it. Contract changes are **bilateral**.
- **Never write backend internals into this repo** — no server IPs, DB schema/credentials,
  deploy topology, or ERP coupling. Negotiation/working drafts stay local (see `.gitignore`);
  only ratified, sanitized interfaces are committed.
- Before publishing anything under `contract/`, sanity-check it against the rules above —
  a public repo's history cannot be un-published.

## Process & communication

- **One topic at a time.** The owner drives the agenda; answer the current question thoroughly,
  then stop. Do not fan out parallel questions or append "next up…" nudges. Parked ideas wait
  in the backlog until he pulls them.
- **Confirm load-bearing assumptions cheaply — then act.** Before building a large artifact on
  an uncertain, load-bearing, or owner's-call premise, surface it in one line and confirm.
  For anything certain, obvious, or squarely in scope, just act — do not over-ask.
- **Keep build output quiet.** Redirect build/run logs and surface them only on failure; don't
  dump green output into the conversation.

## Design & data modeling

- **The business rule drives the schema, not the reverse** ("the shoe fits the foot"). Design
  from the business rule first, then reconcile to the database as migrations / `ALTER`s. If the
  existing schema conflicts, the schema changes — don't quietly weaken the rule to fit legacy
  columns, and don't impose premises without listening to the intended model first.
- **Server-side is the source of truth.** The client runs on untrusted machines; it never
  decides entitlements on its own. Don't add client-side gating a spoofed client could skip.

## Writing & language

- **English** for all artifacts — code, comments, docs, commit messages, this file. Brainstorm
  chat may be in another language; produced artifacts are English (public portfolio audience).
- **Never self-deprecate in outward-facing copy.** Portfolio, docs, and public backlog are read
  by hiring managers and staff engineers. Frame incomplete work as **forward roadmap**
  ("upcoming in the backlog", "next up"), never as a deficit. Avoid "gap", "pending",
  "placeholder", "TODO", "limitation" in public copy. Stay truthful — point the framing at the
  roadmap, don't claim done what isn't. (This does not gag honest *internal* engineering notes.)

## Operational notes

- **The developer's own machine is the source of truth for real-disk state.** Assistant tooling
  may run sandboxed, so files written by a launched dev process (caches, device state) may not
  reflect onto the real disk. When filesystem reality matters, defer to the developer's own view
  rather than asserting from tool output.
