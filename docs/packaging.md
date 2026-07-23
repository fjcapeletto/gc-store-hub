# Packaging, distribution & self-update

How the hub goes from source to an installable, self-updating Windows app — **without
any manual per-release babysitting**. Push a tag; CI does the rest.

## The two feeds (don't confuse them)

- **Hub self-update feed** — where the *hub's own binaries* come from. Public (the client
  is public), hosted on this repo's **GitHub Releases**. Configured by
  `Update:GithubRepo` in `appsettings.json`.
- **App catalog feed** — where the *store's app catalog* comes from. That's the private
  backend (`Catalog:Url`), device-authorized. Nothing to do with releasing the hub.

This doc is about the first one.

## Prerequisites (local builds only; CI installs these itself)

- .NET 10 SDK
- Velopack CLI: `dotnet tool install -g vpk --version 1.2.0` (keep it matching the
  `Velopack` NuGet version in the csproj)

## Release via CI — the normal path (no local tools, no Claude)

The whole pipeline is [`.github/workflows/release.yml`](../.github/workflows/release.yml).
To ship a version:

```bash
# bump, commit, then tag with the version and push the tag
git tag v1.0.1
git push origin v1.0.1
```

On that tag push, GitHub Actions (on a Windows runner):

1. checks out and installs .NET 10 + the Velopack CLI;
2. derives the version from the tag (`v1.0.1` → `1.0.1`);
3. `dotnet publish` self-contained `win-x64`;
4. `vpk download github` — pulls the **previous** release so the next step can compute a
   **delta** (small updates, not a full 50 MB each time);
5. `vpk pack` — builds `Setup.exe`, the full/delta `.nupkg`, and the feed metadata;
6. `vpk upload github` — publishes them to this repo's **GitHub Releases** under the tag.

That's it. `GITHUB_TOKEN` is provided automatically by Actions (the workflow requests
`contents: write`); no extra secrets are required for an unsigned build.

### What users get

- **New install:** download `Setup.exe` from the latest GitHub Release, run it (per-user,
  no admin). It installs to `%LocalAppData%\GabrielCapelettoStoreHub`, adds Desktop + Start
  Menu shortcuts, opens once, then lives in the tray.
- **Update:** an already-installed hub checks the GitHub feed on launch, downloads any newer
  version in the background, and swaps it in on the next quit/relaunch — never mid-use. Only
  a *real install* updates; running from source or an un-installed publish is a no-op
  (`UpdateManager.IsInstalled` is false there).
- **Uninstall:** Windows Settings → Apps → "Gabriel Capeletto Store" → Uninstall.

## Release locally — the fallback path

If you need to produce an installer by hand (no CI):

```powershell
./build/build-installer.ps1 -Version 1.0.1
# -> artifacts/releases/GabrielCapelettoStoreHub-win-Setup.exe
```

To also publish it to GitHub Releases from your machine (so installed hubs pick it up),
after packing:

```powershell
vpk upload github --repoUrl https://github.com/fjcapeletto/gc-store-hub `
  --token <a GitHub PAT with repo scope> --publish --releaseName "GC Store 1.0.1" --tag v1.0.1 -o artifacts/releases
```

For deltas locally, run `vpk download github ... -o artifacts/releases` before `vpk pack`.

## Versioning

The version is the git tag (`vMAJOR.MINOR.PATCH`). Velopack requires versions to move
forward — never re-tag an existing version. There's no version to bump in a file; the tag
is the source of truth.

## Code signing (production)

Unsigned installers trip Windows SmartScreen ("Windows protected your PC" → *More info* →
*Run anyway*). For production, sign with an Authenticode certificate stored as a GitHub
Actions **secret** (never committed — the signing key belongs in a secret manager, same
rule as the license-signing key). Pass it to `vpk pack --signParams "..."`; see the
commented block at the bottom of the workflow.

## Notes

- The mock installer built for local testing bundles `appsettings.Development.json`
  (which points `Catalog:Url` at `http://localhost:8080`). Real/CI builds do **not** carry
  a dev override — the catalog URL comes from the committed `appsettings.json` (a
  placeholder) or an env var / secret manager (`GCSTORE_Catalog__Url`).
- Build artifacts (`artifacts/`, `publish/`, `Releases/`) are git-ignored — never commit
  binaries.
