# Gabriel Capeletto Store — Hub Client

The public Windows desktop client for the **Gabriel Capeletto Store**: a private
software hub pre-installed on the refurbished laptops sold by GabrielCapeletto LLC.

The hub behaves like an app store for the device: apps are versioned, updated on
the user's terms, and can be uninstalled and reinstalled from the hub. It adds
value to the device and opens a subscription revenue line. The store vitrine is
**visible to every device** — entitlement gates the *right to install*, not
visibility. A machine without store access sees the full catalog with apps
**locked** and an offer to buy access (the sales hook); the server withholds the
package/license. See [`contract/`](contract/) for the wire boundary.

> **Status:** early scaffolding. This is the *walking-skeleton* stage — an empty
> hub with no real apps yet. The first milestone is the distribution pipeline:
> publish something and watch it appear on machines that already have the hub.

## Scope of this repository

This repo is **only the public hub client**, split from the rest of the system
along a strict trust boundary:

- **Public (this repo):** the thin per-OS hub client.
- **Private (separate repo):** the entitlement/store backend — signing keys,
  enforcement logic, catalog administration. None of it lives here.

Because entitlements are resolved server-side and the client already runs on
untrusted customer machines, publishing the client is safe: there are no secrets
to protect on this side.

## Platforms

Windows 11 first. The hub client is intentionally thin and per-OS; the
entitlement service it talks to is OS-agnostic, leaving the door open to a Linux
hub later.

## Tech

C# / .NET, Windows-first. The desktop UI framework is a deliberately deferred
decision and is **not** fixed by this scaffolding.

## Building

_To be documented once the first project is added._ Real configuration is
supplied via `dotnet user-secrets` in development and environment variables /
a secret manager in production — never committed. See `.env.example` /
`appsettings.example.json` (added alongside the first project) for the shape.

## License

Proprietary. All rights reserved. The source is public for portfolio purposes
only and grants **no** permission to use, copy, modify, or distribute it. See
[LICENSE](LICENSE).
