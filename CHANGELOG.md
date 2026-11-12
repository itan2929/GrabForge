# Changelog

## 0.3.2 — 2026-11-13

- Put runtime libraries, updater, help and licenses inside `app/`, keeping `GrabForge.exe` as the only root file after startup.
- Keep settings/presets/history portable; place new tool installations under `data/tools/` while retaining existing `tools/` installations.
- Preserve old updater compatibility and back up obsolete flat-layout files under `data/backups/` without touching unrelated files.
- Add an offline help entry and automated native package boot checks without a global .NET installation.
- Separate development outputs, QA data, logs and the local profile into documented folders.

## 0.3.1 — 2026-11-10

- Fix clipped right corners on mode and settings tabs by using a header layout that respects tab margins.
- Fit format-table columns to the available panel width, including scrollbar space, and keep short container names readable.
- Avoid unnecessary horizontal scrolling in the video/audio format tables.

## 0.3.0 — 2026-11-10

- Redesign the native interface with graphite surfaces, restrained selection accents, consistent spacing and a dark Windows title bar.
- Unify inputs, dropdown menus, checkboxes, tabs, table headers and scrollbars, including visible keyboard focus and disabled states.
- Add source-detail hierarchy, format counts, aligned numerical columns and concise empty states while retaining the bounded window layout.
- Preserve download modes, all effect settings, presets and portable tool/update behavior.

## 0.2.2 — 2026-11-10

- Fix analysis failing in the format selection screen when yt-dlp returns null chapters, subtitles or automatic captions.
- Treat missing optional collections as empty and cover both null and omitted metadata with regression tests.

## 0.2.1 — 2026-11-10

- Fix updater path traversal validation looping indefinitely for application base directories ending in a separator.
- Canonicalize the portable data root and reject paths that cannot reach the expected root.
- Add a regression check with a real trailing-separator update request.
- Retry bounded transient Windows file locks when switching verified tool installations.
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
