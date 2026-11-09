# Project conventions

- Windows-only native C# / WPF application. No WebView, local HTTP server, or Python runtime.
- Preserve feature parity with the archived ClipForge web prototype; do not edit that project. The new native product is named GrabForge.
- Use .NET CLI for build, tests, and self-contained win-x64 publication.
- Keep UI, domain logic, external process management, and updates independently testable.
- Tools are downloaded into the portable application directory and invoked by absolute path.
- Settings, history and versioned JSON presets must survive updates. Never commit user data or tools.
- Release version: 0.1.0. Do not publish a release until the documented acceptance gates pass.
