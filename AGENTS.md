# Project conventions

- Windows-only native C# / WPF application. No WebView, local HTTP server, or Python runtime.
- Preserve feature parity with the archived web prototype; do not edit that project. The native product is named GrabForge.
- Use .NET CLI for build, tests, and self-contained win-x64 publication.
- Keep UI, domain logic, external process management, and updates independently testable.
- Tools are downloaded into the portable application directory and invoked by absolute path.
- Settings, history and versioned JSON presets must survive updates. Never commit user data or tools.
- Product version comes from Directory.Build.props. Do not publish until the documented acceptance gates pass.
- Keep primary pages within the window. Lists and logs may scroll; use sections/tabs for settings, not a long page.
- Keep visible copy to labels, actions and short state messages. Put extended guidance in tooltips or documentation.
