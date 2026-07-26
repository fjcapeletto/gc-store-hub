# Authoring a GC Store app (packaging convention)

How an **app developer** builds and packages an app the hub can install, launch, update and
uninstall. The hub owns the runtime; this is the contract your package must satisfy. Clear-phase
(unencrypted); hardening later adds signing/encryption **without changing this shape**.

## What a GC Store app is

A **portable, self-contained Windows x64** program that:
- runs standalone **from its own folder** (nothing pre-installed — no .NET/VC++ runtime install),
- needs **no admin / no installer**, writes only under **its own folder** or
  `%LocalAppData%` / `%APPDATA%` (no registry / no system dirs),
- is **launched by the hub** (the hub is the gate — it checks entitlement, then starts your exe).

If it can't run by just unzipping a folder and double-clicking the exe, it's not packaged right.

## The package: a ZIP with `app.json` at the root

```
weather-1.0.0.zip
├── app.json          ← manifest, at the ARCHIVE ROOT (not inside a subfolder)
├── Weather.exe       ← the entry exe the hub launches
├── *.dll             ← its dependencies
└── assets/…          ← anything else it needs
```

`app.json`:
```json
{
  "appId": "com.gc.weather",
  "version": "1.0.0",
  "entryExe": "Weather.exe",
  "args": [],
  "displayName": "Weather"
}
```

- `appId` / `version` **must match** what the backend registers for this artifact (and the catalog
  entry). Bump `version` for every update.
- `entryExe` is a path **relative to the zip root**.
- The **whole zip is `sha-256`-hashed**; `app.json` is covered too, so it can't be tampered in transit.

**Prerequisites / drivers (forward-looking).** If your app needs a **driver** (e.g. a serial/RS-232
device on rugged hardware) it will declare it in a `requires` list; the hub installs that driver **for
free, ungated** (the customer owns the hardware → owns its drivers), with a one-time elevation for the
driver step only — your app stays portable and un-elevated. Note **opening a COM port is user-level**
and needs none of this; only installing an *absent* driver does. (The `requires` shape lands when
delivery is built — flagged so you design hardware apps with it in mind.)

## Developer steps — worked example (.NET app)

1. **Publish self-contained, single-folder, win-x64** (bundles the runtime — no external install):
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true -o out
   ```
   `out/` now has `Weather.exe` + its runtime DLLs.

2. **Add `app.json`** to `out/` (next to the exe).

3. **Zip the CONTENTS of `out/`** so `app.json` and `Weather.exe` sit at the **zip root** (do NOT zip
   the `out` folder itself — the hub reads `app.json` from the archive root):
   ```powershell
   Compress-Archive -Path out\* -DestinationPath weather-1.0.0.zip
   ```

4. **Compute the `sha-256`** (the backend needs it):
   ```powershell
   (Get-FileHash weather-1.0.0.zip -Algorithm SHA256).Hash
   ```

5. **Hand the backend** the zip + its metadata: `appId`, `version`, `sha-256`, `sizeBytes`. The backend
   stores it (an `app_package` row + the file on disk) and serves it from `POST /v1/delivery/{appId}`.
   **That's where your job ends** — you produce the artifact; the server publishes it; the hub delivers
   and installs it.

## Any language — bundle its runtime

The rule is language-agnostic: **produce a portable folder that runs with nothing pre-installed.** The
only trick per language is bundling the runtime so the machine doesn't need it:

| Stack | How to make it a portable folder | `entryExe` points at |
|-------|----------------------------------|----------------------|
| .NET | `dotnet publish -r win-x64 --self-contained true` | your `App.exe` |
| **Java** | `jpackage --type app-image` (or `jlink` a minimal JRE + your jars) — bundles the JRE + a launcher | the generated `App.exe` launcher |
| **Python** | **PyInstaller** `--onedir` (or Nuitka / cx_Freeze) — bundles the interpreter + deps | the frozen `App.exe` |
| Node / Electron | `electron-builder --dir`, or `pkg` for CLI | the produced `App.exe` |
| Go / Rust / C++ | compile to a native exe (+ its DLLs) | your `App.exe` |

What does **not** work: shipping `.jar`/`.py`/`.js` source and assuming Java/Python/Node is installed —
the refurb machine may not have it. **Freeze/bundle the runtime.** The hub launches the produced exe;
it never runs `java -jar` or `python app.py`.

Trade-off: bundling a runtime makes each app bigger (a JRE/Python/.NET runtime is tens of MB). That's
the price of "unzip and run anywhere". If bloat ever matters we can add a **shared-runtime** model
(the hub guarantees a runtime is present so apps ship lighter) — deferred; self-contained is the robust
default for now.

## What the hub does with your zip (the contract you're satisfying)

1. `POST /v1/delivery/{appId}` → gets a short-lived signed URL + your `sha-256`.
2. Downloads the zip, **verifies the `sha-256`** (mismatch → rejected, not installed).
3. Extracts to `%LocalAppData%\GabrielCapelettoStore\apps\{appId}\{version}\`.
4. Reads `app.json` → learns `entryExe`.
5. **Open** → checks the lease (entitlement/expiry), then `Process.Start(entryExe)` with the **working
   directory = that version folder**.
6. **Update** → same flow into a new `{version}\` folder, swaps the active one, deletes the old.
7. **Uninstall** → deletes the app's folder. (Reinstallable while the device stays granted.)

## Developer checklist

- [ ] **win-x64, self-contained** — runs on a clean machine with nothing installed.
- [ ] **Portable** — no admin, no installer, no `HKLM`/registry, no writes outside its folder /
      `%LocalAppData%` / `%APPDATA%`.
- [ ] **No hardcoded install path** — the app runs from wherever the hub extracted it. Resolve your own
      location at runtime (`AppContext.BaseDirectory` in .NET, `GetModuleFileName` in native) and read
      bundled assets relative to the exe.
- [ ] **`app.json` at the zip root**, `entryExe` relative, `appId`/`version` matching the backend.
- [ ] **Single entry exe** the hub starts (it can spawn children itself after that).
- [ ] Clear-phase: **unsigned is OK** (Windows may warn on first launch; signing comes in hardening).
- [ ] Keep it lean (helps future delta updates).

## Versioning

`version` in `app.json` = the app's published version. It must equal the catalog `version` and the
`app_package` the backend serves for that release. Bumping it is what makes the hub offer an update.
