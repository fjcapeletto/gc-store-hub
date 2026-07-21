# Mock catalog

Sample catalog documents used to exercise the distribution pipeline while the real
backend does not exist yet. The hub fetches whatever URL is set in
`Catalog:Url` (see `src/GabrielCapelettoStore.Hub/appsettings.json`).

- `catalog.json` — two sample apps → the hub shows populated shelves.
- `catalog.empty.json` — no apps → the hub shows the empty-shelves state.

## How to serve it (pick one)

### Option 1 — local static server (quickest for dev)

Serve this folder over HTTP and point the hub at it via an untracked
`appsettings.Development.json` (which overrides the committed placeholder URL):

```jsonc
// src/GabrielCapelettoStore.Hub/appsettings.Development.json  (git-ignored)
{
  "Catalog": { "Url": "http://localhost:8080/catalog.json" }
}
```

Any static server works, for example:

```bash
# .NET global tool
dotnet tool install -g dotnet-serve
dotnet-serve --directory mock --port 8080
```

Edit `catalog.json`, relaunch the hub → the change appears. That is the pipeline.

### Option 2 — remote host (proves cross-machine propagation)

Upload `catalog.json` somewhere reachable over HTTPS (a raw file in a repo, a
blob/bucket, a CDN) and set that URL as `Catalog:Url`. Now any machine running
the hub picks up edits to that one file. Note: raw file hosts often cache for a
few minutes, so propagation is not instant.

## Environment-variable override (prod-style)

`Catalog:Url` can also be supplied without touching any file, via an environment
variable — this is the secret-manager / deployment path:

```
GCSTORE_Catalog__Url=https://catalog.example.com/catalog.json
```
