# Releasing GrabForge

1. Update the product version in `Directory.Build.props`, Windows manifest identity in `src/GrabForge.Desktop/app.manifest`, and the changelog. Runtime version detection, user agent, release manifest and ZIP names derive from the product version.
2. Run `scripts/build.ps1 -Publish` on Windows with .NET 10 SDK. Run both integration commands in `ACCEPTANCE.md` using managed tools.
3. Review the acceptance evidence, native controls at default/minimum sizes and portable update/rollback behavior. `tests/native-smoke.ps1` runs native checks through an installed Cua driver; pass its executable, target PID/window, socket and local demo URL. Keep the old web prototype unchanged.
4. Push source to GitHub and require the Windows workflow to pass. It builds/tests/publishes, installs isolated tools and runs actual media/queue checks.
5. Download the workflow artifact. Audit the ZIP: root `GrabForge.exe`, updater, `release.json`, licenses; no `data/`, `tools/`, PDBs, development SDK or user information. Verify its SHA-256 sidecar.
6. Publish a GitHub release tagged `vX.Y.Z` with `GrabForge-vX.Y.Z-win-x64.zip` and its `.sha256` file. Prefer the exact CI artifact; record the source commit in the release notes.
7. Read back public asset names and GitHub `digest` fields and compare the ZIP SHA-256. Smoke-test a downloaded release copy. App update lookup requires the exact product ZIP name and a valid upstream SHA-256 digest.

The first release is MIT-licensed source plus a self-contained .NET/WPF application. External tools are acquired from upstream on demand, not bundled. Preserve runtime notices when changing SDK/runtime versions.

The logo source is `assets/grabforge.png`. `scripts/prepare-icon.ps1` packages it as a multi-resolution ICO. Screenshots in `docs/images/` must come from the actual app and use self-generated demo content.

Portable updates preserve `data/` and `tools/`, retain a `.update-backup-*` folder, and record `data/update-result.txt` or `data/update-error.txt`. A failed file replacement rolls back files already applied. Backups can be removed manually after verifying a successful update.
