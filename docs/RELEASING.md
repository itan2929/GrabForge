# Releasing GrabForge

1. Update the product version in `Directory.Build.props`, Windows manifest identity in `src/GrabForge.Desktop/app.manifest`, and the changelog. Runtime version detection, user agent, release manifest and ZIP names derive from the product version.
2. Run `scripts/build.ps1 -Publish` on Windows with .NET 10 SDK. Run both integration commands in `ACCEPTANCE.md` using managed tools.
3. Review the acceptance evidence, native controls at default/minimum sizes and portable update/rollback behavior. `tests/native-smoke.ps1` runs native checks through an installed Cua driver; pass its executable, target PID/window, socket and local demo URL. Keep the old web prototype unchanged.
   Update QA must include an application base directory with its normal trailing separator, not only a CLI-supplied normalized folder. v0.1.0/v0.2.0 require manual ZIP overwrite because their already-installed helper is affected.
4. Push source to GitHub and require the Windows workflow to pass. It builds/tests/publishes, installs isolated tools and runs actual media/queue checks.
5. Download the workflow artifact. Audit the ZIP: only `GrabForge.exe`, `app/` and the compatibility `release.json` at root. Runtime files, updater, help and notices live inside `app/`; no user data, tools, PDBs or development SDK. Verify its SHA-256 sidecar. Build outputs live in `artifacts/packages/`; native package boot is checked by `tests/check-package.ps1` with an unrelated working directory and invalid global .NET paths.
6. Publish a GitHub release tagged `vX.Y.Z` with `GrabForge-vX.Y.Z-win-x64.zip` and its `.sha256` file. Prefer the exact CI artifact; record the source commit in the release notes.
7. Read back public asset names and GitHub `digest` fields and compare the ZIP SHA-256. Smoke-test a downloaded release copy. App update lookup requires the exact product ZIP name and a valid upstream SHA-256 digest.

The first release is MIT-licensed source plus a self-contained .NET/WPF application. External tools are acquired from upstream on demand, not bundled. Preserve runtime notices when changing SDK/runtime versions.

The logo source is `assets/grabforge.png`. `scripts/prepare-icon.ps1` packages it as a multi-resolution ICO. Screenshots in `docs/images/` must come from the actual app and use self-generated demo content.

Portable updates preserve `data/` and legacy `tools/`, retain backups under `data/backups/`, and record `data/update-result.txt` or `data/update-error.txt`. A failed file replacement rolls back files already applied. Backups can be removed manually after verifying a successful update.

For layout 2 (v0.3.2+), the root EXE is a standard SDK apphost bound to `app/GrabForge.dll`, using only the adjacent private runtime. `PortableLayout.FindRoot` keeps settings and output relative to the portable root. The duplicate root manifest is required by v0.2.1-v0.3.1 helpers and moves into `app/` on startup. Startup backs up only the exact old package paths recorded in the embedded v0.3.1 inventory; it never recursively removes user folders. Verify migration with the actual old published helper, including a trailing-separator root and unchanged settings/presets/history/tool sentinels. Also verify the new helper is found under `app/updater/` and backs up inside `data/`.

Keep local profiles in `.local/GrabForge/`, not a versioned build output. Do not copy old QA artifacts into a relocated checkout; source, SDK, selected profile and selected tool cache are sufficient. The old web prototype remains read-only.
