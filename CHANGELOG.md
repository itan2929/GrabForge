# Changelog

## 0.2.1 — 2026-11-10

- Fix updater path traversal validation looping indefinitely for application base directories ending in a separator.
- Canonicalize the portable data root and reject paths that cannot reach the expected root.
- Add a regression check with a real trailing-separator update request.
- Document ZIP-overwrite migration from v0.1.0/v0.2.0; those installed helpers cannot repair themselves through the affected update path.

## 0.2.0 — 2026-11-10

- Add a generated GrabForge logo and multi-resolution Windows icon.
- Replace the long download form with a bounded two-column workspace and tabbed settings.
- Place compressor and normalization side by side, preserving both simple and advanced controls.
- Reduce visible guidance to labels, actions and short state messages.
- Refresh the README with branding, native screenshots, quick start and development instructions.
- Remove retired names from source/protocols and isolate obsolete local build artifacts outside the project.
- Keep unknown-codec media formats selectable and retain validation on inactive settings tabs.

## 0.1.0 — 2026-11-09

- New Windows-native C# / WPF application, independent of the earlier web prototype.
- Portable tool acquisition and updates, versioned presets, media jobs and FFmpeg audio processing.
- Self-contained application packaging and GitHub release update workflow.
- Native tool installation, preset operations and real portable update/restart verified on Windows.
- Audio measurements, metadata/chapter combinations, playlist range and queue controls covered by runtime integration checks.

Release status and verification evidence: `docs/ACCEPTANCE.md`.
