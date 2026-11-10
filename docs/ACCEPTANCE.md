# GrabForge acceptance gates

Product name: **GrabForge**. The old web application is a read-only specification reference, not a runtime dependency.

## v0.2.2 optional metadata correction

- Reproduced the reported failure with `https://www.youtube.com/watch?v=-0ff3wyGicg`: yt-dlp returned 43 formats and `chapters: null`; the native format-summary renderer threw `NullReferenceException` before populating either list.
- Normalize explicit null collections in the media model, including subtitles and automatic captions. Regression cases cover null, empty and omitted metadata, with independent video/audio selection retained. All 34 unit tests pass.
- Retried the exact URL through the published native candidate: title, video formats, audio formats and the final `.mp4` filename preview appeared without a dialog. Read-only analysis; no third-party media download was required.

## v0.2.1 updater correction

- The actual v0.1.0 GUI update exposed a trailing-separator bug missed by the earlier CLI fixture: `AppContext.BaseDirectory` ends in a separator and parent-path traversal never reached an exact string match. The old helper was stopped in the isolated QA installation; user data was intact.
- Portable roots and updater requests are now canonicalized. Parent traversal uses case-insensitive comparison and rejects an unreachable root instead of looping.
- Added an update request with a trailing separator and a bounded regression check. A Windows CI tool-installation lock also exposed the need for bounded file-operation retries, covered with real lock/release and cancellation tests. Unit suite now has 30 tests.
- Existing v0.1.0/v0.2.0 helpers cannot apply this repair through the affected path. The documented migration is a full ZIP overwrite, preserving `data/` and `tools/`; v0.2.0 is marked prerelease/superseded.

## v0.2.0 — Windows x64 (2026-11-10)

- Generated logo embedded in the sidebar, WPF window icon and multi-resolution PE application icon (16–256 px).
- Bounded workspace with side navigation, side-by-side format panels and output/effects/metadata/advanced tabs. Pages have no outer vertical ScrollViewer; media lists and logs retain their own scrolling.
- Compressor and normalization remain independent, with separate simple/advanced controls and complete preset round-trips.
- Removed retired product names from current source, internal process markers and generated build caches. Old generated packages/QA output are isolated outside the project; recursive archive deletion was denied by the execution policy even after user authorization.
- README includes the generated logo, real native screenshot, quick start, portable updates, capabilities, development and license links.
- 27 unit tests pass. Re-ran actual media, metadata, chapter, playlist and queue-control integration checks successfully with the renamed process markers.
- Native smoke procedure: `tests/native-smoke.ps1`, using the generated local demo served by `tests/GrabForge.Integration ... serve artifacts/qa`. Checks settings tabs, preset save/apply, live preview, effect values, minimum-size control bounds and native download/postprocessing.
- Native smoke passed: default size and 1040×640, all effect controls visible; local demo acquired and processed to MP3. An invalid numeric entry stayed blocked after changing settings tabs, then accepted the corrected value.
- Verify the final CI artifact, data-preserving v0.1.0 ZIP migration, corrected helper restart and published asset digest before completion.

## v0.1.0 baseline (2026-11-09)

| Requirement | Evidence |
|---|---|
| Native UI, no browser/server/Python/npm requirements | Self-contained ZIP extracted outside source tree; `GrabForge.exe` opened a WPF window |
| Media/playlist analysis | Managed yt-dlp analyzed local WAV, three-item HTML5 playlist and real SoundCloud media with fractional duration (160.466 seconds); native analysis also succeeded |
| Separate video/audio choices and three modes | Argument tests, native mode controls; actual video-only processing removed audio from a combined source |
| Containers and audio conversion | MP3/M4A/Opus/FLAC/WAV produced and probed; requested sampling rates retained; invariant-culture encoding/filter arguments tested |
| Compressor + two-pass normalization | Quiet fixture −41.75 LUFS → approximately −14 LUFS in all five audio formats; copied video packet hash unchanged |
| Simple/advanced audio controls | Native conversion toggle, compressor/normalization tabs, ratio edit to 3.5 and normalization preset reflected as −14 in detail fields |
| Filename preview | Actual yt-dlp template evaluation, final MP3 extension in CLI and native UI; playlist index evaluated to `02` |
| Subtitles, chapters, artwork, metadata | Local video/audio jobs retained cover art, two chapters and video subtitles, saved SRT/JPEG/info.json sidecars; full file and both chapter files measured near −20 LUFS |
| Opus artwork | Picture metadata survived conversion and normalization; ffprobe confirmed attached artwork |
| Advanced yt-dlp controls | Tests cover browser cookies, proxy, limits, retries, fragment concurrency, archive, SponsorBlock, remux and extra arguments |
| Multiple URLs and playlist range | Three actual downloads under concurrency=2; HTML5 playlist range selected only item 2 |
| Queue controls and history | Live process pause/resume, cancellation while paused, successful retry, immediate cancellation, final-state persistence and clearing tested |
| Complete presets | All option fields round-tripped; schema rejection and independent clones tested; native save/apply/export/import/delete verified with file readback |
| Managed tools | Native “prepare all” installed yt-dlp 2026.10.26, BtbN FFmpeg and Deno v2.9.8; upstream SHA-256 and executable probes checked before switching |
| Update discovery and verification | Fake HTTPS GitHub feed selected GrabForge ZIP and validated downloaded SHA-256; missing/unverifiable assets rejected |
| Actual portable update | Detached published helper waited for old PID, replaced installed binaries, wrote success record and restarted native GrabForge under a new PID; installed DLL hash matched package |
| Preservation and rollback | Data/tools sentinel files and presets survived actual update; locked destination test restored prior application files; unsafe paths, data/tools entries, wrong version and corrupted hash rejected |
| Build and packaging | Desktop/updater build without warnings; 26 unit tests pass; ZIP/checksum generated from fresh staging, local data/tools and PDB files excluded; .NET distribution notices included |

Reproduce automated media/queue checks:

```powershell
dotnet run --project tests/GrabForge.Integration -- install artifacts/qa
dotnet run --project tests/GrabForge.Integration -- verify artifacts/qa
```

The integration checks use self-generated media and a local HTTP fixture. The shipped application does not run a web server. Real-site analysis was read-only; no third-party media download was needed for QA.

## External limitations

- Codec/container compatibility, available site formats, subtitles and chapters depend on the source. Selecting an incompatible combination reports the upstream error.
- Browser cookie access, login requirements and SponsorBlock results depend on the browser/site/service. Argument handling is tested; external authentication and category matches are not universally guaranteed.
- WAV does not offer the same artwork/chapter features as MP3/M4A. Audio bitrate settings are disabled for lossless WAV/FLAC. AAC bitrate targets and VBR rates depend on the encoder and source.
- Conversion before yt-dlp metadata/chapter processing and subsequent audio effects can encode lossy audio twice. Keep the original when an archival source is needed.
- Pause/resume applies to the active external process tree. A transition between processes may briefly have no pausable process.
- Releases are unsigned Windows x64 builds. Executables/tools must be in a writable directory; settings/tools are retained locally.

## Publication gate

- Public MIT repository: https://github.com/itan2929/GrabForge
- Full Windows CI (build, 26 unit tests, packaging, managed tool install, media and queue integration) passed: https://github.com/itan2929/GrabForge/actions/runs/42613721008
- Each release candidate must pass CI before its artifact is released. Release assets/checksums are read back after publication.
