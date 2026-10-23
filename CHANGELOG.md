# Changelog

All notable changes to CircleQuickLauncher will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-10-23

### Added

- Hover zoom: anchor and app circles scale up smoothly when hovered (default 30%, configurable 0–100%).
- Configurable edge position: place the anchor anywhere along the chosen edge (default 75%).
- Display selection: stay on the primary display or pick any connected monitor.
- Fluid center-preserving scale animation for hover and expand/collapse (XAML RenderTransform + Storyboard).
- AddAppWindow centered like Settings, transparent and borderless.
- README updated with v1.2 features and screenshots.

### Fixed

- SearchBox positioned below/above the anchor to prevent spurious pointer-exit on click.

## [1.0.0] - 2026-10-23

Initial release.

### Added

- Floating edge-peeking circular anchor (Left / Right / Bottom / Free-floating).
- Radial app menu fan-out on hover with staggered spring animations (opacity + scale).
- System tray resident with context menu (Settings, Add App, Exit).
- Add App dialog: browse Start Menu catalog or paste a custom executable path.
- Custom PNG icon import (validated min dimensions, non-empty, copied to local cache).
- Search / filter inside the expanded menu.
- Settings window: edge position, peek fraction, arc radius, max items, anchor/child circle sizes, collapse delay, start-with-windows toggle.
- Single-instance enforcement via named mutex + named pipe (focus existing instance on relaunch).
- Autorun support (HKCU Run key for portable; StartupTask for MSIX).
- Crash logging to `%APPDATA%\CircleQuickLauncher\logs\`.
- Schema-versioned JSON config (`settings.json`, `apps.json`) with atomic writes and sanitization.
- Pure, unit-tested layout math in `CircleQuickLauncher.Core` (66 tests).
- DPI-aware, multi-monitor work-area math.
- WinUI 3 unpackaged self-contained app (Windows App SDK 1.8, .NET 8, x64).
- Inno Setup installer script, portable publish profile, and MSIX packaging.
- GitHub Actions CI (build + test) and Release (tag → artifacts → GitHub Release).

### Known limitations

- Window transparency is limited by the WinUI 3 unpackaged composition model; the launcher renders on an opaque dark backdrop (`#F20F172A`) rather than a true transparent acrylic surface.
- No UI automation tests; hover/expand behavior verified manually.
- x64 only.

[1.2.0]: https://github.com/deadbrainviv/CircleQuickLauncher/releases/tag/v1.2.0
[1.0.0]: https://github.com/deadbrainviv/CircleQuickLauncher/releases/tag/v1.0.0
