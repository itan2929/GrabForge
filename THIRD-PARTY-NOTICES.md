# Third-party components

GrabForge's own source is MIT licensed. This does not relicense external components.

## Distributed runtime and test dependencies

- .NET / WPF: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf — MIT; the package's `licenses/` directory retains the chosen .NET distribution's license and third-party notices.
- xUnit / Microsoft.NET.Test.Sdk: development/test dependencies, not included in the application package. See each NuGet package's license.

## Tools acquired at the user's request

Tools are stored separately in `tools/` and are not included in the application ZIP.

- yt-dlp official Windows executable: https://github.com/yt-dlp/yt-dlp — consult the upstream LICENSE and bundled third-party notices for the executable distribution (not only source license).
- FFmpeg GPL Windows build by BtbN: https://github.com/BtbN/FFmpeg-Builds and https://ffmpeg.org/legal.html — selected archive is a GPL build with upstream third-party dependencies. Source/build instructions are available upstream.
- Deno: https://github.com/denoland/deno — MIT and bundled third-party notices.

Each installed tool directory records the exact asset URL, SHA-256 and license/source URL in `SOURCE.txt` and installation metadata. Licenses present in downloaded archives are retained.

Do not assume that downloading tools separately removes their licensing requirements if you redistribute those tools yourself.
